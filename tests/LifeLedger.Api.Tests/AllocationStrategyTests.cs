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

/// <summary>Protects category-level allocation and dated target-strategy behaviour.</summary>
public sealed class AllocationStrategyTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the suite with an isolated local API host.</summary>
    public AllocationStrategyTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Ensures the allocation combines holdings across brokers by category and excludes opted-out wealth.</summary>
    [Fact]
    public async Task Dashboard_groups_included_assets_by_category_and_reports_target_drift()
    {
        var scenarioId = await SeedScenarioAsync();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            db.Assets.AddRange(
                new Asset { ScenarioId = scenarioId, Name = "World ETF at IBKR", Kind = AssetKind.Etf, CustomCategory = "ETF World", CurrentValue = 250m, Currency = "EUR" },
                new Asset { ScenarioId = scenarioId, Name = "World ETF at Fortis", Kind = AssetKind.Etf, CustomCategory = "ETF World", CurrentValue = 50m, Currency = "EUR" },
                new Asset { ScenarioId = scenarioId, Name = "Gold ETC", Kind = AssetKind.Etf, CustomCategory = "Gold", CurrentValue = 50m, Currency = "EUR" },
                new Asset { ScenarioId = scenarioId, Name = "Paris apartment", Kind = AssetKind.RealEstate, CurrentValue = 900_000m, Currency = "EUR", IsIncludedInPortfolioAllocation = false });
            await db.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        var created = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/allocation-strategies", new AllocationStrategyRequest(
            "Long-term allocation", "A dated personal strategy", new DateOnly(2026, 1, 1), null,
            [new AllocationStrategyTargetRequest("ETF World", 80m, 3m), new AllocationStrategyTargetRequest("Gold", 20m, 3m)]));
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        var dashboard = await client.GetFromJsonAsync<DashboardResponse>($"/api/scenarios/{scenarioId}/dashboard", jsonOptions);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(dashboard);
        Assert.Equal(2, dashboard.Allocation.Count);
        Assert.Equal(300m, dashboard.Allocation.Single(slice => slice.Name == "ETF World").Value);
        Assert.Equal(85.71m, dashboard.Allocation.Single(slice => slice.Name == "ETF World").Percentage);
        Assert.NotNull(dashboard.AllocationStrategy);
        Assert.Equal("Long-term allocation", dashboard.AllocationStrategy.Name);
        Assert.Equal(AllocationTargetState.Overweight, dashboard.AllocationStrategy.Targets.Single(target => target.Category == "ETF World").State);
        Assert.Equal(AllocationTargetState.Underweight, dashboard.AllocationStrategy.Targets.Single(target => target.Category == "Gold").State);
    }

    /// <summary>Ensures two strategy versions cannot silently claim the same effective period.</summary>
    [Fact]
    public async Task Strategy_versions_cannot_overlap()
    {
        var scenarioId = await SeedScenarioAsync();
        using var client = _factory.CreateClient();
        var first = new AllocationStrategyRequest("2026 strategy", null, new DateOnly(2026, 1, 1), null, [new AllocationStrategyTargetRequest("Cash", 100m, 5m)]);
        var second = new AllocationStrategyRequest("Overlapping strategy", null, new DateOnly(2026, 7, 1), null, [new AllocationStrategyTargetRequest("Cash", 100m, 5m)]);

        var initialResponse = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/allocation-strategies", first);
        var overlapResponse = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/allocation-strategies", second);

        Assert.Equal(HttpStatusCode.Created, initialResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, overlapResponse.StatusCode);
    }

    /// <summary>Creates the minimal profile and scenario required by dashboard calculations.</summary>
    private async Task<Guid> SeedScenarioAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var profile = new Profile { DisplayName = $"Allocation test {Guid.NewGuid():N}", BaseCurrency = "EUR", BirthDate = new DateOnly(1990, 1, 1), ExpectedLifespan = 90 };
        var scenario = new FinancialScenario { Profile = profile, Name = "Allocation scenario", Assumptions = new SimulationAssumptions() };
        db.Scenarios.Add(scenario);
        await db.SaveChangesAsync();
        return scenario.Id;
    }
}
