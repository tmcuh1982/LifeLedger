using System.Text.Json;
using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Provides field-level errors for an invalid asset dossier without leaking persistence details.</summary>
public sealed class AssetDossierValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("The asset dossier is invalid.")
{
    /// <summary>Validation messages indexed by request field.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Creates, updates, and reads a complete asset dossier through one transactional Interface.</summary>
public interface IAssetDossierService
{
    /// <summary>Creates an asset, characteristic profile, and debt allocations atomically.</summary>
    Task<AssetDossierResponse> CreateAsync(Guid scenarioId, AssetDossierRequest request, CancellationToken cancellationToken);
    /// <summary>Replaces every editable part of an existing asset dossier atomically.</summary>
    Task<AssetDossierResponse?> UpdateAsync(Guid assetId, AssetDossierRequest request, CancellationToken cancellationToken);
    /// <summary>Returns an asset dossier with freshly calculated performance, or null when absent.</summary>
    Task<AssetDossierResponse?> GetAsync(Guid assetId, CancellationToken cancellationToken);
}

/// <summary>EF Core Implementation of the complete asset-dossier Module.</summary>
public sealed class AssetDossierService(LifeLedgerDbContext db, IAssetProfileCatalog profiles, ICurrencyService currencies, IAssetValuationHistoryService valuations) : IAssetDossierService
{
    /// <inheritdoc />
    public async Task<AssetDossierResponse> CreateAsync(Guid scenarioId, AssetDossierRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Scenarios.AnyAsync(scenario => scenario.Id == scenarioId, cancellationToken))
            throw new KeyNotFoundException("Scenario not found.");
        await ValidateAsync(scenarioId, null, request, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var asset = new Asset { ScenarioId = scenarioId };
        ApplyCore(asset, request);
        ApplyProfile(asset, request);
        asset.LiabilityLinks = (request.LiabilityAllocations ?? []).Select(allocation => new AssetLiabilityLink { LiabilityId = allocation.LiabilityId, AllocationRate = allocation.AllocationRate }).ToList();
        db.Assets.Add(asset);
        await valuations.RecordAsync(asset, request.ValuedOn, request.ValuationSource, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return (await GetAsync(asset.Id, cancellationToken))!;
    }

    /// <inheritdoc />
    public async Task<AssetDossierResponse?> UpdateAsync(Guid assetId, AssetDossierRequest request, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.Include(candidate => candidate.CharacteristicProfile).Include(candidate => candidate.LiabilityLinks).FirstOrDefaultAsync(candidate => candidate.Id == assetId, cancellationToken);
        if (asset is null) return null;
        await ValidateAsync(asset.ScenarioId, asset.Id, request, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        ApplyCore(asset, request);
        ApplyProfile(asset, request);
        ApplyLiabilityAllocations(asset, request.LiabilityAllocations ?? []);
        await valuations.RecordAsync(asset, request.ValuedOn, request.ValuationSource, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await GetAsync(asset.Id, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AssetDossierResponse?> GetAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await db.Assets.AsNoTracking()
            .Include(candidate => candidate.CharacteristicProfile)
            .Include(candidate => candidate.LiabilityLinks).ThenInclude(link => link.Liability)
            .FirstOrDefaultAsync(candidate => candidate.Id == assetId, cancellationToken);
        return asset is null ? null : new AssetDossierResponse(asset, CalculatePerformance(asset));
    }

    /// <summary>Validates profile schema, ownership, allocation range, and aggregate debt allocation.</summary>
    private async Task ValidateAsync(Guid scenarioId, Guid? assetId, AssetDossierRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(await profiles.ValidateAsync(request.ProfileDefinitionKey, request.ProfileDefinitionVersion, request.ProfileValues, cancellationToken), StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["An asset name is required."];
        if (request.Currency.Trim().Length != 3 || !request.Currency.Trim().All(char.IsAsciiLetter)) errors["currency"] = ["Use a three-letter ISO 4217 currency code."];
        if (request.CurrentValue < 0 || request.PurchasePrice < 0 || request.AcquisitionCosts < 0) errors["amounts"] = ["Asset amounts cannot be negative."];

        var allocations = request.LiabilityAllocations ?? [];
        if (allocations.Select(allocation => allocation.LiabilityId).Distinct().Count() != allocations.Count)
            errors["liabilityAllocations"] = ["A liability can only be linked once to the same asset."];
        if (allocations.Any(allocation => allocation.AllocationRate is <= 0 or > 1))
            errors["liabilityAllocations"] = ["Each linked share must be greater than 0% and at most 100%."];

        var liabilityIds = allocations.Select(allocation => allocation.LiabilityId).ToArray();
        if (liabilityIds.Length > 0)
        {
            var validIds = await db.Liabilities.AsNoTracking().Where(liability => liability.ScenarioId == scenarioId && liabilityIds.Contains(liability.Id)).Select(liability => liability.Id).ToListAsync(cancellationToken);
            if (validIds.Count != liabilityIds.Length) errors["liabilityAllocations"] = ["Every linked liability must belong to the same scenario."];

            var existing = await db.AssetLiabilityLinks.AsNoTracking()
                .Where(link => liabilityIds.Contains(link.LiabilityId) && (assetId == null || link.AssetId != assetId))
                .GroupBy(link => link.LiabilityId)
                .Select(group => new { LiabilityId = group.Key, Rate = group.Sum(link => link.AllocationRate) })
                .ToDictionaryAsync(entry => entry.LiabilityId, entry => entry.Rate, cancellationToken);
            if (allocations.Any(allocation => existing.GetValueOrDefault(allocation.LiabilityId) + allocation.AllocationRate > 1m))
                errors["liabilityAllocations"] = ["A liability cannot be allocated above 100% across assets."];
        }
        if (errors.Count > 0) throw new AssetDossierValidationException(errors);
    }

    /// <summary>Copies strongly typed financial facts from the request into the persisted asset.</summary>
    private static void ApplyCore(Asset asset, AssetDossierRequest request)
    {
        asset.Name = request.Name.Trim();
        asset.Kind = request.Kind;
        asset.CustomCategory = string.IsNullOrWhiteSpace(request.CustomCategory) ? null : request.CustomCategory.Trim();
        asset.CurrentValue = request.CurrentValue;
        asset.PurchasePrice = request.PurchasePrice;
        asset.AcquisitionCosts = request.AcquisitionCosts;
        asset.PurchasedOn = request.PurchasedOn;
        asset.ValuedOn = request.ValuedOn;
        asset.ValuationSource = string.IsNullOrWhiteSpace(request.ValuationSource) ? null : request.ValuationSource.Trim();
        asset.ExpectedAnnualReturn = request.ExpectedAnnualReturn;
        asset.Volatility = request.Volatility;
        asset.IsLiquid = request.IsLiquid;
        asset.Ticker = string.IsNullOrWhiteSpace(request.Ticker) ? null : request.Ticker.Trim().ToUpperInvariant();
        asset.Quantity = request.Quantity;
        asset.IsIncludedInPortfolioAllocation = request.IsIncludedInPortfolioAllocation;
        asset.CapitalGainsTaxRate = request.CapitalGainsTaxRate;
        asset.CapitalGainsTaxCountryCode = string.IsNullOrWhiteSpace(request.CapitalGainsTaxCountryCode) ? null : request.CapitalGainsTaxCountryCode.Trim().ToUpperInvariant();
        asset.Currency = request.Currency.Trim().ToUpperInvariant();
    }

    /// <summary>Creates, updates, or removes the versioned characteristic profile without exposing its JSON storage.</summary>
    private void ApplyProfile(Asset asset, AssetDossierRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ProfileDefinitionKey))
        {
            if (asset.CharacteristicProfile is not null) db.AssetCharacteristicProfiles.Remove(asset.CharacteristicProfile);
            asset.CharacteristicProfile = null;
            return;
        }

        asset.CharacteristicProfile ??= new AssetCharacteristicProfile();
        asset.CharacteristicProfile.DefinitionKey = request.ProfileDefinitionKey.Trim().ToLowerInvariant();
        asset.CharacteristicProfile.DefinitionVersion = request.ProfileDefinitionVersion!.Value;
        asset.CharacteristicProfile.ValuesJson = JsonSerializer.Serialize(request.ProfileValues ?? new Dictionary<string, JsonElement>());
    }

    /// <summary>Updates relationship rows in place so unchanged links do not create duplicate tracked keys.</summary>
    private void ApplyLiabilityAllocations(Asset asset, IReadOnlyList<AssetLiabilityAllocationRequest> allocations)
    {
        var obsolete = asset.LiabilityLinks.ToDictionary(link => link.LiabilityId);
        foreach (var allocation in allocations)
        {
            if (obsolete.Remove(allocation.LiabilityId, out var existing))
                existing.AllocationRate = allocation.AllocationRate;
            else
                asset.LiabilityLinks.Add(new AssetLiabilityLink { AssetId = asset.Id, LiabilityId = allocation.LiabilityId, AllocationRate = allocation.AllocationRate });
        }
        db.AssetLiabilityLinks.RemoveRange(obsolete.Values);
    }

    /// <summary>Calculates gain and equity from typed amounts; dynamic profile fields never enter financial calculations.</summary>
    private AssetPerformanceResponse CalculatePerformance(Asset asset)
    {
        var basis = asset.PurchasePrice + asset.AcquisitionCosts;
        var gain = asset.CurrentValue - basis;
        decimal linkedDebt = 0m;
        foreach (var link in asset.LiabilityLinks.Where(link => link.Liability is not null))
        {
            // Every debt is converted before allocation so multi-currency equity remains meaningful.
            linkedDebt += currencies.Convert(link.Liability!.OutstandingBalance, link.Liability.Currency, asset.Currency) * link.AllocationRate;
        }
        return new AssetPerformanceResponse(asset.Currency, basis, gain, basis > 0 ? gain / basis : null, Math.Round(linkedDebt, 4), Math.Round(asset.CurrentValue - linkedDebt, 4));
    }
}
