using LifeLedger.Api.Data;

namespace LifeLedger.Api.Services;

/// <summary>Advances existing business data to the version that supports optional planned asset sales.</summary>
public sealed class PlannedAssetSaleDataMigration : IDataSchemaMigration
{
    /// <inheritdoc />
    public int FromVersion => 5;
    /// <inheritdoc />
    public int ToVersion => 6;

    /// <inheritdoc />
    public Task ApplyAsync(LifeLedgerDbContext db, CancellationToken cancellationToken)
    {
        // Existing scenarios need no synthetic sale: the new collection is intentionally empty until the user plans one.
        return Task.CompletedTask;
    }
}
