using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Records and reads the local total-value history of individual assets.</summary>
public interface IAssetValuationHistoryService
{
    /// <summary>Adds or replaces the valuation for one asset and calendar date in the active unit of work.</summary>
    Task RecordAsync(Asset asset, DateOnly? valuedOn, string? source, CancellationToken cancellationToken);
    /// <summary>Returns an asset's valuation points in chronological order.</summary>
    Task<IReadOnlyList<AssetValuationSnapshot>> ListAsync(Guid assetId, CancellationToken cancellationToken);
}

/// <summary>EF Core implementation that keeps at most one total-value observation per asset and day.</summary>
public sealed class AssetValuationHistoryService(LifeLedgerDbContext db) : IAssetValuationHistoryService
{
    /// <inheritdoc />
    public async Task RecordAsync(Asset asset, DateOnly? valuedOn, string? source, CancellationToken cancellationToken)
    {
        var date = valuedOn ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var snapshot = db.AssetValuationSnapshots.Local.FirstOrDefault(candidate => candidate.AssetId == asset.Id && candidate.ValuedOn == date)
            ?? await db.AssetValuationSnapshots.FirstOrDefaultAsync(candidate => candidate.AssetId == asset.Id && candidate.ValuedOn == date, cancellationToken);

        if (snapshot is null)
        {
            snapshot = new AssetValuationSnapshot { AssetId = asset.Id, ValuedOn = date };
            db.AssetValuationSnapshots.Add(snapshot);
        }

        // A later correction on the same day replaces the point instead of creating a misleading duplicate.
        snapshot.Value = asset.CurrentValue;
        snapshot.Currency = asset.Currency.Trim().ToUpperInvariant();
        snapshot.Source = string.IsNullOrWhiteSpace(source) ? "Manual estimate" : source.Trim();
        snapshot.RecordedAt = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AssetValuationSnapshot>> ListAsync(Guid assetId, CancellationToken cancellationToken) =>
        await db.AssetValuationSnapshots.AsNoTracking()
            .Where(snapshot => snapshot.AssetId == assetId)
            .OrderBy(snapshot => snapshot.ValuedOn)
            .ToListAsync(cancellationToken);
}
