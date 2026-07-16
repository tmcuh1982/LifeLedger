using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Captures actual net-worth observations locally when the application starts.</summary>
public interface INetWorthHistoryService
{
    /// <summary>Records the current wealth of every baseline scenario without interrupting application startup.</summary>
    Task CaptureAsync(CancellationToken cancellationToken = default);
}

/// <summary>Calculates and stores net-worth history in each profile's base currency.</summary>
public sealed class NetWorthHistoryService(
    LifeLedgerDbContext db,
    ICurrencyService currencies,
    ILogger<NetWorthHistoryService> logger) : INetWorthHistoryService
{
    /// <inheritdoc />
    public async Task CaptureAsync(CancellationToken cancellationToken = default)
    {
        var baselineScenarios = await db.Scenarios
            .Where(scenario => scenario.IsBaseline)
            // Assets and liabilities are independent collections; splitting avoids a cartesian product.
            .AsSplitQuery()
            .Include(scenario => scenario.Profile)
            .Include(scenario => scenario.Assets)
            .Include(scenario => scenario.Liabilities)
            .ToListAsync(cancellationToken);

        foreach (var scenario in baselineScenarios)
        {
            try
            {
                var profile = scenario.Profile!;
                // Consolidate every asset and liability before calculating the real observed net worth.
                var assets = scenario.Assets.Sum(asset => currencies.Convert(asset.CurrentValue, asset.Currency, profile.BaseCurrency));
                var liabilities = scenario.Liabilities.Sum(liability => currencies.Convert(liability.OutstandingBalance, liability.Currency, profile.BaseCurrency));
                db.NetWorthSnapshots.Add(new NetWorthSnapshot
                {
                    ProfileId = profile.Id,
                    NetWorth = Math.Round(assets - liabilities, 2),
                    Currency = profile.BaseCurrency
                });
            }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
            {
                // Missing local exchange rates must not stop the self-hosted application from starting.
                logger.LogWarning(exception, "Skipped net-worth history capture for scenario {ScenarioId}", scenario.Id);
            }
        }

        if (db.ChangeTracker.HasChanges())
        {
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
