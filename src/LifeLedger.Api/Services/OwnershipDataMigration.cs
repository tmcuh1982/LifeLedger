using LifeLedger.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Introduces explicit personal ownership and debt-responsibility shares for existing financial records.</summary>
public sealed class OwnershipDataMigration : IDataSchemaMigration
{
    /// <inheritdoc />
    public int FromVersion => 6;
    /// <inheritdoc />
    public int ToVersion => 7;

    /// <inheritdoc />
    public async Task ApplyAsync(LifeLedgerDbContext db, CancellationToken cancellationToken)
    {
        // Legacy records always represented amounts wholly owned or owed by the profile.
        await db.Assets.Where(asset => asset.OwnershipRate <= 0m).ExecuteUpdateAsync(
            update => update.SetProperty(asset => asset.OwnershipRate, 1m), cancellationToken);
        await db.Liabilities.Where(liability => liability.ResponsibilityRate <= 0m).ExecuteUpdateAsync(
            update => update.SetProperty(liability => liability.ResponsibilityRate, 1m), cancellationToken);
    }
}
