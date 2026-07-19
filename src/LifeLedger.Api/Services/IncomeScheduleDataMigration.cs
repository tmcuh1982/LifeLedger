using LifeLedger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Initialises the annual reference amount for income records created before flexible schedules existed.</summary>
public sealed class IncomeScheduleDataMigration : IDataSchemaMigration
{
    /// <inheritdoc />
    public int FromVersion => 2;
    /// <inheritdoc />
    public int ToVersion => 3;

    /// <inheritdoc />
    public async Task ApplyAsync(LifeLedgerDbContext db, CancellationToken cancellationToken)
    {
        // Existing records remain monthly; the annual reference makes later mode changes lossless and explicit.
        var incomes = await db.Incomes
            .Where(income => income.AnnualAmount == 0m)
            .ToListAsync(cancellationToken);
        foreach (var income in incomes) income.AnnualAmount = income.MonthlyAmount * 12m;
    }
}
