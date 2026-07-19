using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Creates, validates and edits explicit future sales within one scenario aggregate.</summary>
public interface IPlannedAssetSaleService
{
    /// <summary>Creates a planned sale after validating all referenced scenario entries.</summary>
    Task<PlannedAssetSale> CreateAsync(Guid scenarioId, PlannedAssetSaleRequest request, CancellationToken cancellationToken = default);
    /// <summary>Updates one planned sale while preserving its identity and scenario ownership.</summary>
    Task<PlannedAssetSale?> UpdateAsync(Guid saleId, PlannedAssetSaleRequest request, CancellationToken cancellationToken = default);
    /// <summary>Deletes one planned sale without deleting its asset.</summary>
    Task<bool> DeleteAsync(Guid saleId, CancellationToken cancellationToken = default);
}

/// <summary>EF Core implementation of the planned asset-sale persistence boundary.</summary>
public sealed class PlannedAssetSaleService(LifeLedgerDbContext db) : IPlannedAssetSaleService
{
    /// <inheritdoc />
    public async Task<PlannedAssetSale> CreateAsync(Guid scenarioId, PlannedAssetSaleRequest request, CancellationToken cancellationToken = default)
    {
        if (!await db.Scenarios.AnyAsync(scenario => scenario.Id == scenarioId, cancellationToken)) throw new KeyNotFoundException();
        var sale = new PlannedAssetSale { ScenarioId = scenarioId };
        await ApplyAsync(sale, request, cancellationToken);
        db.AssetSales.Add(sale);
        await db.SaveChangesAsync(cancellationToken);
        return sale;
    }

    /// <inheritdoc />
    public async Task<PlannedAssetSale?> UpdateAsync(Guid saleId, PlannedAssetSaleRequest request, CancellationToken cancellationToken = default)
    {
        var sale = await db.AssetSales.FirstOrDefaultAsync(candidate => candidate.Id == saleId, cancellationToken);
        if (sale is null) return null;
        await ApplyAsync(sale, request, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return sale;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid saleId, CancellationToken cancellationToken = default)
    {
        var sale = await db.AssetSales.FirstOrDefaultAsync(candidate => candidate.Id == saleId, cancellationToken);
        if (sale is null) return false;
        db.AssetSales.Remove(sale);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Validates cross-entry ownership and copies only user-editable values onto the stored entity.</summary>
    private async Task ApplyAsync(PlannedAssetSale sale, PlannedAssetSaleRequest request, CancellationToken cancellationToken)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors.Add("Give this planned sale a name.");
        if (request.HappensOn < await db.Scenarios.Where(scenario => scenario.Id == sale.ScenarioId).Select(scenario => scenario.StartsOn).SingleAsync(cancellationToken))
            errors.Add("The sale date cannot be before the scenario starts.");
        if (!request.UseProjectedValue && request.GrossSalePrice <= 0m) errors.Add("Enter a positive sale price or use the projected value.");
        if (request.SellingCosts < 0m) errors.Add("Selling costs cannot be negative.");
        if (request.CapitalGainsTaxRate is < 0m or > 1m) errors.Add("The capital-gains tax rate must be between 0% and 100%.");
        if (request.Currency.Length != 3 || !request.Currency.All(char.IsAsciiLetter)) errors.Add("Use a three-letter currency code.");

        var assetExists = await db.Assets.AnyAsync(asset => asset.Id == request.AssetId && asset.ScenarioId == sale.ScenarioId, cancellationToken);
        if (!assetExists) errors.Add("Choose an asset from this scenario.");
        var duplicateExists = await db.AssetSales.AnyAsync(candidate => candidate.AssetId == request.AssetId && candidate.Id != sale.Id, cancellationToken);
        if (duplicateExists) errors.Add("This asset already has a planned sale.");

        if (request.Destination == AssetSaleDestination.Asset)
        {
            if (request.DestinationAssetId is null || request.DestinationAssetId == request.AssetId ||
                !await db.Assets.AnyAsync(asset => asset.Id == request.DestinationAssetId && asset.ScenarioId == sale.ScenarioId, cancellationToken))
                errors.Add("Choose another asset from this scenario as the destination.");
        }
        if (request.Destination == AssetSaleDestination.InvestmentPlan &&
            (request.DestinationInvestmentPlanId is null || !await db.Investments.AnyAsync(plan => plan.Id == request.DestinationInvestmentPlanId && plan.ScenarioId == sale.ScenarioId, cancellationToken)))
            errors.Add("Choose an investment plan from this scenario as the destination.");

        if (errors.Count > 0) throw new ArgumentException(string.Join(" ", errors));

        sale.Name = request.Name.Trim();
        sale.AssetId = request.AssetId;
        sale.HappensOn = request.HappensOn;
        sale.UseProjectedValue = request.UseProjectedValue;
        sale.GrossSalePrice = Math.Max(0m, request.GrossSalePrice);
        sale.SellingCosts = request.SellingCosts;
        sale.CapitalGainsTaxRate = request.CapitalGainsTaxRate;
        sale.CapitalGainsTaxCountryCode = string.IsNullOrWhiteSpace(request.CapitalGainsTaxCountryCode) ? null : request.CapitalGainsTaxCountryCode.Trim().ToUpperInvariant();
        sale.RepayLinkedLiabilities = request.RepayLinkedLiabilities;
        sale.Destination = request.Destination;
        sale.DestinationAssetId = request.Destination == AssetSaleDestination.Asset ? request.DestinationAssetId : null;
        sale.DestinationInvestmentPlanId = request.Destination == AssetSaleDestination.InvestmentPlan ? request.DestinationInvestmentPlanId : null;
        sale.Currency = request.Currency.Trim().ToUpperInvariant();
        sale.Notes = request.Notes?.Trim() ?? string.Empty;
    }
}
