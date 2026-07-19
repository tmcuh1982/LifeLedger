using System.Net;
using System.Net.Http.Json;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using LifeLedger.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies dated recurring-expense steps in calculation and persistence.</summary>
public sealed class ExpenseScheduleTests : IClassFixture<LifeLedgerApiFactory>
{
    /// <summary>Hosts the API with an isolated temporary database.</summary>
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates the test class with its shared in-process API host.</summary>
    public ExpenseScheduleTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Uses the current amount before a step and the replacement amount from its effective month.</summary>
    [Fact]
    public void Future_amount_replaces_the_current_amount_on_its_date()
    {
        var expense = ExampleExpense(indexedToInflation: false);
        var service = new ExpenseScheduleService();

        Assert.Equal(2_000m, service.AmountForOccurrence(expense, new DateOnly(2031, 6, 1), 0.02m));
        Assert.Equal(3_000m, service.AmountForOccurrence(expense, new DateOnly(2031, 7, 1), 0.02m));
    }

    /// <summary>Restarts inflation from the explicit future amount instead of applying five earlier years twice.</summary>
    [Fact]
    public void Inflation_restarts_from_each_explicit_amount_step()
    {
        var expense = ExampleExpense(indexedToInflation: true);
        var service = new ExpenseScheduleService();

        Assert.Equal(3_000m, service.AmountForOccurrence(expense, new DateOnly(2031, 7, 1), 0.02m));
        Assert.Equal(3_060m, Math.Round(service.AmountForOccurrence(expense, new DateOnly(2032, 7, 1), 0.02m), 2));
    }

    /// <summary>Persists an unlimited dated schedule with its owning recurring expense.</summary>
    [Fact]
    public async Task Api_persists_future_expense_amounts()
    {
        var scenarioId = Guid.NewGuid();
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
            database.Scenarios.Add(new FinancialScenario
            {
                Id = scenarioId,
                Name = "Cost of living steps",
                Profile = new Profile { DisplayName = "Expense schedule profile" },
                Assumptions = new SimulationAssumptions()
            });
            await database.SaveChangesAsync();
        }

        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync($"/api/scenarios/{scenarioId}/expenses", new
        {
            name = "Cost of living",
            kind = "Recurring",
            frequency = "Monthly",
            monthlyAmount = 2_000m,
            indexedToInflation = true,
            startsOn = "2026-07-01",
            endsOn = "2076-07-01",
            currency = "EUR",
            amountChanges = new[]
            {
                new { effectiveOn = "2031-07-01", amount = 3_000m },
                new { effectiveOn = "2041-07-01", amount = 4_000m }
            }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var verificationScope = _factory.Services.CreateAsyncScope();
        var verificationDatabase = verificationScope.ServiceProvider.GetRequiredService<LifeLedgerDbContext>();
        var saved = await verificationDatabase.Expenses.Include(expense => expense.AmountChanges).SingleAsync(expense => expense.ScenarioId == scenarioId);
        Assert.Equal([3_000m, 4_000m], saved.AmountChanges.OrderBy(change => change.EffectiveOn).Select(change => change.Amount));
    }

    /// <summary>Creates the representative 2,000-to-3,000 euro planning example.</summary>
    private static Expense ExampleExpense(bool indexedToInflation) => new()
    {
        Kind = ExpenseKind.Recurring,
        Frequency = RecurrenceFrequency.Monthly,
        StartsOn = new DateOnly(2026, 7, 1),
        MonthlyAmount = 2_000m,
        IndexedToInflation = indexedToInflation,
        AmountChanges = [new ExpenseAmountChange { EffectiveOn = new DateOnly(2031, 7, 1), Amount = 3_000m }]
    };
}
