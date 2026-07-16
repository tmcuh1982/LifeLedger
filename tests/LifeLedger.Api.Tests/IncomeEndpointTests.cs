using System.Net;
using System.Net.Http.Json;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using LifeLedger.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies public API behaviour for income entries.</summary>
public sealed class IncomeEndpointTests : IClassFixture<LifeLedgerApiFactory>
{
    /// <summary>Hosts the API with an isolated temporary database.</summary>
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the test class with its shared in-process API host.</summary>
    public IncomeEndpointTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Protects clients from receiving a misleading 405 response for a valid PUT route.</summary>
    [Fact]
    public async Task Updating_an_unknown_income_returns_not_found_not_method_not_allowed()
    {
        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/incomes/{Guid.NewGuid()}", new
        {
            name = "Salary", kind = "Salary", monthlyAmount = 1, annualGrowthRate = 0,
            startsOn = "2026-01-01", isTaxable = true, taxRate = 0, currency = "EUR"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>Deletes every persisted financial record while leaving the API available for a future backup restore.</summary>
    [Fact]
    public async Task Deleting_all_data_removes_the_local_profile()
    {
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            database.Profiles.Add(new Profile { DisplayName = "Temporary test profile" });
            await database.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        using var response = await client.DeleteAsync("/api/data");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        Assert.Equal(0, await verificationDatabase.Profiles.CountAsync());
    }

    /// <summary>Rejects an invalid replacement import before it can remove the current local profile.</summary>
    [Fact]
    public async Task Invalid_replacement_import_preserves_existing_data()
    {
        var existingProfileId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            database.Profiles.Add(new Profile { Id = existingProfileId, DisplayName = "Existing local profile" });
            await database.SaveChangesAsync();
        }

        var invalidDocument = new LifeLedgerExport(
            1,
            DateTimeOffset.UtcNow,
            new Profile { DisplayName = "", BaseCurrency = "EUR", ExpectedLifespan = 81 },
            []);
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/import", new ImportRequest(invalidDocument, ReplaceExisting: true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        Assert.True(await verificationDatabase.Profiles.AnyAsync(profile => profile.Id == existingProfileId));
    }

    /// <summary>Persists the selected sex while retaining a neutral default for older data and backups.</summary>
    [Fact]
    public async Task Updating_a_profile_persists_the_selected_sex()
    {
        var profileId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            database.Profiles.Add(new Profile { Id = profileId, DisplayName = "Profile test", BirthDate = new DateOnly(1990, 6, 15) });
            await database.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        using var response = await client.PutAsJsonAsync($"/api/profiles/{profileId}", new
        {
            id = profileId,
            displayName = "Profile test",
            birthDate = "1990-06-15",
            sex = "Female",
            homeCountryCode = "PL",
            baseCurrency = "EUR",
            expectedLifespan = 84,
            childrenCount = 0,
            careers = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        Assert.Equal(ProfileSex.Female, await verificationDatabase.Profiles.Where(profile => profile.Id == profileId).Select(profile => profile.Sex).SingleAsync());
    }

    /// <summary>Returns the local net-worth observations that belong to the selected scenario's profile.</summary>
    [Fact]
    public async Task Net_worth_history_is_available_for_a_scenario()
    {
        var profileId = Guid.NewGuid();
        var scenarioId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            database.Profiles.Add(new Profile { Id = profileId, DisplayName = "History test profile" });
            database.Scenarios.Add(new FinancialScenario { Id = scenarioId, ProfileId = profileId, Name = "Reference", IsBaseline = true, Assumptions = new SimulationAssumptions { ScenarioId = scenarioId } });
            database.NetWorthSnapshots.Add(new NetWorthSnapshot { ProfileId = profileId, NetWorth = 123_456.78m, Currency = "EUR" });
            await database.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        using var response = await client.GetAsync($"/api/scenarios/{scenarioId}/net-worth-history");
        var history = await response.Content.ReadFromJsonAsync<NetWorthSnapshotResponse[]>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var snapshot = Assert.Single(history!);
        Assert.Equal(123_456.78m, snapshot.NetWorth);
        Assert.Equal("EUR", snapshot.Currency);
    }

    /// <summary>Captures the baseline scenario's current assets less liabilities at application startup.</summary>
    [Fact]
    public async Task Net_worth_capture_records_the_baseline_scenario_value()
    {
        var profileId = Guid.NewGuid();
        var scenarioId = Guid.NewGuid();
        await using var scope = _factory.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        database.Profiles.Add(new Profile { Id = profileId, DisplayName = "Capture test profile", BaseCurrency = "EUR" });
        database.Scenarios.Add(new FinancialScenario
        {
            Id = scenarioId,
            ProfileId = profileId,
            Name = "Reference",
            IsBaseline = true,
            Assumptions = new SimulationAssumptions { ScenarioId = scenarioId },
            Assets = [new Asset { ScenarioId = scenarioId, Name = "Cash", Kind = AssetKind.Cash, CurrentValue = 10_000m, Currency = "EUR" }],
            Liabilities = [new Liability { ScenarioId = scenarioId, Name = "Loan", Kind = LiabilityKind.Loan, OutstandingBalance = 2_500m, Currency = "EUR" }]
        });
        await database.SaveChangesAsync();

        var historyService = scope.ServiceProvider.GetRequiredService<INetWorthHistoryService>();
        await historyService.CaptureAsync();

        var snapshot = await database.NetWorthSnapshots.SingleAsync(item => item.ProfileId == profileId);
        Assert.Equal(7_500m, snapshot.NetWorth);
        Assert.Equal("EUR", snapshot.Currency);
    }
}
