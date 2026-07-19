using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using LifeLedger.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Protects the versioned asset-profile Interface and atomic asset-dossier workflow.</summary>
public sealed class AssetDossierTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the test suite with an isolated local database.</summary>
    public AssetDossierTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Ensures the built-in catalogue exposes stable, versioned profile definitions.</summary>
    [Fact]
    public async Task Profile_catalog_exposes_home_vehicle_and_watch()
    {
        using var scope = _factory.Services.CreateScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IAssetProfileCatalog>();

        var definitions = (await catalog.ListAsync()).Where(definition => !definition.IsCustom).ToArray();

        Assert.Equal(["home", "vehicle", "watch"], definitions.Select(definition => definition.Key));
        Assert.All(definitions, definition => Assert.Equal(1, definition.Version));
        Assert.Contains(definitions.Single(definition => definition.Key == "home").Fields, field => field.Key == "livingArea");
    }

    /// <summary>Ensures editing a custom profile creates a new version while the previous schema remains valid.</summary>
    [Fact]
    public async Task Custom_profile_updates_keep_historical_versions_available()
    {
        var client = _factory.CreateClient();
        var firstRequest = new AssetProfileDefinitionRequest(
            new Dictionary<string, string> { ["en"] = "Bicycle", ["fr"] = "Vélo" },
            [new AssetProfileFieldDefinition("brand", new Dictionary<string, string> { ["en"] = "Brand", ["fr"] = "Marque" }, AssetProfileFieldType.Text, true)]);
        var createResponse = await client.PostAsJsonAsync("/api/asset-profile-definitions", firstRequest);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var created = await createResponse.Content.ReadFromJsonAsync<AssetProfileDefinition>(jsonOptions);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(created);
        Assert.True(created.IsCustom);
        Assert.Equal(1, created.Version);

        var secondRequest = new AssetProfileDefinitionRequest(firstRequest.Labels,
        [
            firstRequest.Fields[0],
            new AssetProfileFieldDefinition("frameSize", new Dictionary<string, string> { ["en"] = "Frame size", ["fr"] = "Taille du cadre" }, AssetProfileFieldType.Number)
        ]);
        var updateResponse = await client.PutAsJsonAsync($"/api/asset-profile-definitions/{created.Key}", secondRequest);
        var updated = await updateResponse.Content.ReadFromJsonAsync<AssetProfileDefinition>(jsonOptions);

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.NotNull(updated);
        Assert.Equal(2, updated.Version);
        Assert.Equal(2, updated.Fields.Count);

        await using var scope = _factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IAssetProfileCatalog>();
        var oldVersionErrors = await catalog.ValidateAsync(created.Key, 1, new Dictionary<string, JsonElement> { ["brand"] = JsonSerializer.SerializeToElement("Example") });
        Assert.Empty(oldVersionErrors);
        var backupHistory = await catalog.ExportCustomHistoryAsync();
        Assert.Contains(backupHistory, definition => definition.Key == created.Key && definition.Version == 1);
        Assert.Contains(backupHistory, definition => definition.Key == created.Key && definition.Version == 2);
    }

    /// <summary>Ensures creation stores the complete dossier and calculates gains and net equity consistently.</summary>
    [Fact]
    public async Task Create_dossier_persists_profile_links_and_performance()
    {
        var scenarioId = await SeedScenarioAsync();
        Guid liabilityId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            var liability = new Liability { ScenarioId = scenarioId, Name = "Mortgage", Kind = LiabilityKind.Mortgage, OutstandingBalance = 120_000m, Currency = "EUR" };
            db.Liabilities.Add(liability);
            await db.SaveChangesAsync();
            liabilityId = liability.Id;
        }

        var request = new AssetDossierRequest(
            "Family home", AssetKind.RealEstate, null, 300_000m, 0.02m, 0.05m, false, null, 0m, 0m, null, "EUR",
            240_000m, 10_000m, new DateOnly(2020, 6, 1), new DateOnly(2026, 7, 17), "Manual estimate",
            "home", 1,
            new Dictionary<string, JsonElement> { ["address"] = JsonSerializer.SerializeToElement("Warsaw"), ["livingArea"] = JsonSerializer.SerializeToElement(95m) },
            [new AssetLiabilityAllocationRequest(liabilityId, 0.5m)]);

        var response = await _factory.CreateClient().PostAsJsonAsync($"/api/scenarios/{scenarioId}/asset-dossiers", request);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var dossier = await response.Content.ReadFromJsonAsync<AssetDossierResponse>(jsonOptions);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(dossier);
        Assert.Equal(250_000m, dossier.Performance.AcquisitionBasis);
        Assert.Equal(50_000m, dossier.Performance.GrossGain);
        Assert.Equal(0.2m, dossier.Performance.GainRate);
        Assert.Equal(60_000m, dossier.Performance.LinkedDebt);
        Assert.Equal(240_000m, dossier.Performance.NetEquity);
        Assert.Equal("home", dossier.Asset.CharacteristicProfile?.DefinitionKey);
        Assert.Single(dossier.Asset.LiabilityLinks);
    }

    /// <summary>Ensures current net worth uses today's estimate rather than the historical purchase price.</summary>
    [Fact]
    public async Task Dashboard_uses_current_asset_value_instead_of_purchase_price()
    {
        var scenarioId = await SeedScenarioAsync();
        var request = new AssetDossierRequest(
            "Appreciated home", AssetKind.RealEstate, null, 260_000m, 0m, 0m, false, null, 0m, 0m, null, "EUR",
            30_000m, 0m, null, new DateOnly(2026, 7, 17), "Manual estimate", null, null, null, []);

        using var client = _factory.CreateClient();
        using var createResponse = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/asset-dossiers", request);
        using var dashboardResponse = await client.GetAsync($"/api/scenarios/{scenarioId}/dashboard");
        await using var dashboardStream = await dashboardResponse.Content.ReadAsStreamAsync();
        using var dashboard = await JsonDocument.ParseAsync(dashboardStream);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, dashboardResponse.StatusCode);
        Assert.Equal(260_000m, dashboard.RootElement.GetProperty("currentNetWorth").GetDecimal());
    }

    /// <summary>Ensures editing a valuation on the same day corrects one history point rather than duplicating it.</summary>
    [Fact]
    public async Task Updating_dossier_replaces_the_same_day_valuation_point()
    {
        var scenarioId = await SeedScenarioAsync();
        var valuationDate = new DateOnly(2026, 7, 17);
        var initialRequest = BasicRequest([]) with { CurrentValue = 20_000m, ValuedOn = valuationDate, ValuationSource = "Personal estimate" };
        using var client = _factory.CreateClient();
        using var createResponse = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/asset-dossiers", initialRequest);
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var created = await createResponse.Content.ReadFromJsonAsync<AssetDossierResponse>(jsonOptions);

        Assert.NotNull(created);
        using var updateResponse = await client.PutAsJsonAsync($"/api/assets/{created.Asset.Id}/dossier", initialRequest with { CurrentValue = 26_000m });
        var valuations = await client.GetFromJsonAsync<AssetValuationSnapshot[]>($"/api/assets/{created.Asset.Id}/valuations");

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var point = Assert.Single(valuations!);
        Assert.Equal(valuationDate, point.ValuedOn);
        Assert.Equal(26_000m, point.Value);
        Assert.Equal("Personal estimate", point.Source);
    }

    /// <summary>Ensures an older backup receives an initial history point from each imported current value.</summary>
    [Fact]
    public async Task Importing_schema_two_backfills_asset_valuation_history()
    {
        var assetName = $"Imported asset {Guid.NewGuid():N}";
        var profile = new Profile { DisplayName = "Imported history", BaseCurrency = "EUR", ExpectedLifespan = 82 };
        var scenario = new FinancialScenario
        {
            Name = "Imported reference",
            Assumptions = new SimulationAssumptions(),
            Assets =
            [
                new Asset
                {
                    Name = assetName, Kind = AssetKind.RealEstate, CurrentValue = 175_000m, Currency = "EUR",
                    ValuedOn = new DateOnly(2025, 12, 31), ValuationSource = "Archived estimate"
                }
            ]
        };
        var document = new LifeLedgerExport(2, DateTimeOffset.UtcNow, profile, [scenario]);

        using var response = await _factory.CreateClient().PostAsJsonAsync("/api/import", new ImportRequest(document));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var snapshot = await db.AssetValuationSnapshots.Include(point => point.Asset).SingleAsync(point => point.Asset!.Name == assetName);
        Assert.Equal(175_000m, snapshot.Value);
        Assert.Equal(new DateOnly(2025, 12, 31), snapshot.ValuedOn);
        Assert.Equal("Archived estimate", snapshot.Source);
    }

    /// <summary>Ensures one debt cannot be allocated above one hundred percent across several assets.</summary>
    [Fact]
    public async Task Create_dossier_rejects_overallocated_liability()
    {
        var scenarioId = await SeedScenarioAsync();
        Guid liabilityId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            var liability = new Liability { ScenarioId = scenarioId, Name = "Shared loan", Kind = LiabilityKind.Loan, OutstandingBalance = 10_000m, Currency = "EUR" };
            var asset = new Asset { ScenarioId = scenarioId, Name = "First asset", Kind = AssetKind.Other, CurrentValue = 20_000m, Currency = "EUR" };
            db.AddRange(liability, asset);
            await db.SaveChangesAsync();
            db.AssetLiabilityLinks.Add(new AssetLiabilityLink { AssetId = asset.Id, LiabilityId = liability.Id, AllocationRate = 0.75m });
            await db.SaveChangesAsync();
            liabilityId = liability.Id;
        }

        var request = BasicRequest([new AssetLiabilityAllocationRequest(liabilityId, 0.5m)]);
        var response = await _factory.CreateClient().PostAsJsonAsync($"/api/scenarios/{scenarioId}/asset-dossiers", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Ensures an unchanged debt link can be updated without duplicate EF tracking keys.</summary>
    [Fact]
    public async Task Update_dossier_changes_an_existing_allocation_in_place()
    {
        var scenarioId = await SeedScenarioAsync();
        Guid assetId;
        Guid liabilityId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            var liability = new Liability { ScenarioId = scenarioId, Name = "Car loan", Kind = LiabilityKind.Loan, OutstandingBalance = 8_000m, Currency = "EUR" };
            var asset = new Asset { ScenarioId = scenarioId, Name = "Car", Kind = AssetKind.Other, CurrentValue = 15_000m, Currency = "EUR" };
            db.AddRange(liability, asset);
            await db.SaveChangesAsync();
            db.AssetLiabilityLinks.Add(new AssetLiabilityLink { AssetId = asset.Id, LiabilityId = liability.Id, AllocationRate = 0.5m });
            await db.SaveChangesAsync();
            assetId = asset.Id;
            liabilityId = liability.Id;
        }

        var request = BasicRequest([new AssetLiabilityAllocationRequest(liabilityId, 0.8m)]);
        var response = await _factory.CreateClient().PutAsJsonAsync($"/api/assets/{assetId}/dossier", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        Assert.Equal(0.8m, await verificationDb.AssetLiabilityLinks.Where(link => link.AssetId == assetId).Select(link => link.AllocationRate).SingleAsync());
    }

    /// <summary>Seeds the minimum aggregate required by the dossier endpoints.</summary>
    private async Task<Guid> SeedScenarioAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var profile = new Profile { DisplayName = "Asset test", BirthDate = new DateOnly(1985, 1, 1), ExpectedLifespan = 90, BaseCurrency = "EUR" };
        var scenario = new FinancialScenario { Profile = profile, Name = $"Asset test {Guid.NewGuid():N}", Assumptions = new SimulationAssumptions() };
        db.Scenarios.Add(scenario);
        await db.SaveChangesAsync();
        return scenario.Id;
    }

    /// <summary>Creates a valid minimal dossier request with the supplied debt allocations.</summary>
    private static AssetDossierRequest BasicRequest(IReadOnlyList<AssetLiabilityAllocationRequest> allocations) => new(
        "Second asset", AssetKind.Other, null, 20_000m, 0m, 0m, false, null, 0m, 0m, null, "EUR",
        15_000m, 0m, null, null, "Manual", null, null, null, allocations);
}
