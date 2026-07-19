using LifeLedger.Api.Data;

namespace LifeLedger.Api.Services;

/// <summary>Marks the introduction of observed banking history as business-data format version four.</summary>
public sealed class BankingDataMigration : IDataSchemaMigration
{
    /// <inheritdoc />
    public int FromVersion => 3;
    /// <inheritdoc />
    public int ToVersion => 4;

    /// <inheritdoc />
    public Task ApplyAsync(LifeLedgerDbContext db, CancellationToken cancellationToken)
    {
        // Existing plans require no inferred values: the new observed-history collections are initially empty.
        return Task.CompletedTask;
    }
}
