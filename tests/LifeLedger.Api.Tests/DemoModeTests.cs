using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Protects the deterministic demo contract used for product regression tests and screenshots.</summary>
public sealed class DemoModeTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the suite with an isolated local API host.</summary>
    public DemoModeTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Restores the exact fixture after users exercise create and delete operations.</summary>
    [Fact]
    public async Task Restore_demo_replaces_mutations_with_the_canonical_dataset()
    {
        using var client = _factory.CreateClient();
        var initialRestore = await client.PostAsync("/api/demo/restore", null);
        var firstState = await ReadFixtureStateAsync();

        // Exercise normal public CRUD routes before asking the demo reset to rebuild the canonical graph.
        var deleteAsset = await client.DeleteAsync("/api/assets/90000000-0000-0000-0000-000000000307");
        var createIncome = await client.PostAsJsonAsync($"/api/scenarios/{DemoDataSeeder.BaselineScenarioId}/incomes", new
        {
            name = "Temporary regression income",
            kind = "Salary",
            monthlyAmount = 1234m,
            amountMode = "Monthly",
            annualAmount = 0m,
            annualGrowthRate = 0m,
            startsOn = "2026-01-01",
            isTaxable = false,
            taxRate = 0m,
            currency = "EUR",
            monthlyAllocations = Array.Empty<object>()
        });
        var mutatedState = await ReadFixtureStateAsync();

        var secondRestore = await client.PostAsync("/api/demo/restore", null);
        var restoredState = await ReadFixtureStateAsync();

        Assert.Equal(HttpStatusCode.OK, initialRestore.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, deleteAsset.StatusCode);
        Assert.Equal(HttpStatusCode.Created, createIncome.StatusCode);
        Assert.Equal(10, mutatedState.Assets);
        Assert.Equal(7, mutatedState.Incomes);
        Assert.Equal(HttpStatusCode.OK, secondRestore.StatusCode);
        Assert.Equal(firstState, restoredState);
    }

    /// <summary>Locks the representative coverage and principal projection values of demo dataset version one.</summary>
    [Fact]
    public async Task Demo_version_one_covers_the_main_financial_cases()
    {
        using var client = _factory.CreateClient();
        await client.PostAsync("/api/demo/restore", null);
        var state = await ReadFixtureStateAsync();
        var jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        jsonOptions.Converters.Add(new JsonStringEnumConverter());

        var dashboard = await client.GetFromJsonAsync<DashboardResponse>($"/api/scenarios/{DemoDataSeeder.BaselineScenarioId}/dashboard", jsonOptions);

        Assert.Equal(new FixtureState(2, 6, 11, 3, 8, 2, 1, 5, 1, 9, 3), state);
        Assert.NotNull(dashboard);
        Assert.Equal(DemoDataSeeder.BaselineScenarioId, dashboard.ScenarioId);
        Assert.Equal("EUR", dashboard.Currency);
        Assert.True(dashboard.CurrentNetWorth > 400_000m);
        Assert.Contains(dashboard.Timeline.SelectMany(year => year.AssetSales), sale => sale.Name == "Vente de l’appartement locatif");
        Assert.Contains(dashboard.Allocation, slice => slice.Name == "ETF Monde");
    }

    /// <summary>Reads only the stable counts that define the fixture's public regression surface.</summary>
    private async Task<FixtureState> ReadFixtureStateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        return new FixtureState(
            await db.Scenarios.CountAsync(),
            await db.Incomes.CountAsync(),
            await db.Assets.CountAsync(),
            await db.Liabilities.CountAsync(),
            await db.Expenses.CountAsync(),
            await db.Investments.CountAsync(),
            await db.AssetSales.CountAsync(),
            await db.Events.CountAsync(),
            await db.BankAccounts.CountAsync(),
            await db.BankTransactions.CountAsync(),
            await db.NetWorthSnapshots.CountAsync());
    }

    /// <summary>Compact structural signature of the canonical demo dataset.</summary>
    private sealed record FixtureState(int Scenarios, int Incomes, int Assets, int Liabilities, int Expenses, int Investments, int AssetSales, int Events, int BankAccounts, int BankTransactions, int NetWorthSnapshots);
}
