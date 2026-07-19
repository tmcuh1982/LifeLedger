using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Backfills the first valuation point for assets created before per-asset history existed.</summary>
public sealed class AssetValuationDataMigration : IDataSchemaMigration
{
    /// <inheritdoc />
    public int FromVersion => 1;
    /// <inheritdoc />
    public int ToVersion => 2;

    /// <inheritdoc />
    public async Task ApplyAsync(LifeLedgerDbContext db, CancellationToken cancellationToken)
    {
        var existingAssetIds = await db.AssetValuationSnapshots.AsNoTracking()
            .Select(snapshot => snapshot.AssetId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var assets = await db.Assets.AsNoTracking()
            .Where(asset => !existingAssetIds.Contains(asset.Id))
            .ToListAsync(cancellationToken);

        foreach (var asset in assets)
        {
            // The asset's own estimate date is authoritative; older records without one start today.
            db.AssetValuationSnapshots.Add(new AssetValuationSnapshot
            {
                AssetId = asset.Id,
                ValuedOn = asset.ValuedOn ?? DateOnly.FromDateTime(DateTime.UtcNow),
                Value = asset.CurrentValue,
                Currency = asset.Currency,
                Source = string.IsNullOrWhiteSpace(asset.ValuationSource) ? "Imported current value" : asset.ValuationSource
            });
        }
    }
}
