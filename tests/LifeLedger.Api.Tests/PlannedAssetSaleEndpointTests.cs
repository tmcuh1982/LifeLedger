using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Protects planned asset-sale ownership, validation and REST update behaviour.</summary>
public sealed class PlannedAssetSaleEndpointTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the suite with the isolated local API host.</summary>
    public PlannedAssetSaleEndpointTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Creates and updates a valid planned sale while rejecting a second sale of the same asset.</summary>
    [Fact]
    public async Task Planned_sale_is_editable_and_unique_per_asset()
    {
        var (scenarioId, assetId) = await SeedScenarioAsync();
        using var client = _factory.CreateClient();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var request = new PlannedAssetSaleRequest("Sell apartment", assetId, new DateOnly(2035, 5, 1), true, 0m, 5_000m, 0.19m, "PL", true, AssetSaleDestination.Cash, null, null, "EUR", "Downsize at retirement");

        var createdResponse = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/asset-sales", request);
        var created = await createdResponse.Content.ReadFromJsonAsync<PlannedAssetSale>(jsonOptions);
        var duplicateResponse = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/asset-sales", request with { Name = "Duplicate" });
        var updatedResponse = await client.PutAsJsonAsync($"/api/asset-sales/{created!.Id}", request with { SellingCosts = 7_500m });
        var updated = await updatedResponse.Content.ReadFromJsonAsync<PlannedAssetSale>(jsonOptions);

        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicateResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, updatedResponse.StatusCode);
        Assert.Equal(7_500m, updated!.SellingCosts);
    }

    /// <summary>Rejects a destination asset owned by another scenario.</summary>
    [Fact]
    public async Task Planned_sale_cannot_transfer_private_value_to_another_scenario()
    {
        var (scenarioId, assetId) = await SeedScenarioAsync();
        var (_, foreignAssetId) = await SeedScenarioAsync();
        using var client = _factory.CreateClient();
        var request = new PlannedAssetSaleRequest("Invalid transfer", assetId, new DateOnly(2035, 5, 1), true, 0m, 0m, 0m, null, false, AssetSaleDestination.Asset, foreignAssetId, null, "EUR", null);

        var response = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/asset-sales", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>Creates a minimal scenario and one property available for a planned sale.</summary>
    private async Task<(Guid ScenarioId, Guid AssetId)> SeedScenarioAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var profile = new Profile { DisplayName = $"Sale test {Guid.NewGuid():N}", BaseCurrency = "EUR", BirthDate = new DateOnly(1990, 1, 1), ExpectedLifespan = 90 };
        var scenario = new FinancialScenario { Profile = profile, Name = "Sale scenario", StartsOn = new DateOnly(2026, 1, 1), Assumptions = new SimulationAssumptions() };
        var asset = new Asset { Scenario = scenario, Name = "Apartment", Kind = AssetKind.RealEstate, CurrentValue = 200_000m, Currency = "EUR" };
        db.Assets.Add(asset);
        await db.SaveChangesAsync();
        return (scenario.Id, asset.Id);
    }
}
