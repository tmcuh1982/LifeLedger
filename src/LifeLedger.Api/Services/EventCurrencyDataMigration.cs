using LifeLedger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Assigns the owning profile currency to life events created before event currencies were persisted.</summary>
public sealed class EventCurrencyDataMigration : IDataSchemaMigration
{
    /// <inheritdoc />
    public int FromVersion => 4;
    /// <inheritdoc />
    public int ToVersion => 5;

    /// <inheritdoc />
    public async Task ApplyAsync(LifeLedgerDbContext db, CancellationToken cancellationToken)
    {
        var scenarios = await db.Scenarios.Include(scenario => scenario.Profile).Include(scenario => scenario.Events).ToListAsync(cancellationToken);
        foreach (var scenario in scenarios)
        {
            // The old engine treated every event as a base-currency amount, so this preserves the exact financial meaning.
            foreach (var scenarioEvent in scenario.Events) scenarioEvent.Currency = scenario.Profile!.BaseCurrency;
        }
    }
}
