using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Provides field-level errors for an invalid liability and its financed-asset allocations.</summary>
public sealed class LiabilityValidationException(IReadOnlyDictionary<string, string[]> errors) : Exception("The liability is invalid.")
{
    /// <summary>Validation messages indexed by request field.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Creates and updates liabilities together with their explicit asset relationships.</summary>
public interface ILiabilityService
{
    /// <summary>Creates one liability and its financed-asset allocations atomically.</summary>
    Task<Liability> CreateAsync(Guid scenarioId, LiabilityRequest request, CancellationToken cancellationToken);
    /// <summary>Replaces the editable liability facts and financed-asset allocations atomically.</summary>
    Task<Liability?> UpdateAsync(Guid liabilityId, LiabilityRequest request, CancellationToken cancellationToken);
}

/// <summary>EF Core implementation of liability ownership and financed-asset relationships.</summary>
public sealed class LiabilityService(LifeLedgerDbContext db) : ILiabilityService
{
    /// <inheritdoc />
    public async Task<Liability> CreateAsync(Guid scenarioId, LiabilityRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Scenarios.AnyAsync(scenario => scenario.Id == scenarioId, cancellationToken))
            throw new KeyNotFoundException("Scenario not found.");
        await ValidateAsync(scenarioId, request, cancellationToken);

        var liability = new Liability { ScenarioId = scenarioId };
        ApplyCore(liability, request);
        ApplyAssetAllocations(liability, request.AssetAllocations ?? []);
        db.Liabilities.Add(liability);
        await db.SaveChangesAsync(cancellationToken);
        return liability;
    }

    /// <inheritdoc />
    public async Task<Liability?> UpdateAsync(Guid liabilityId, LiabilityRequest request, CancellationToken cancellationToken)
    {
        var liability = await db.Liabilities.Include(candidate => candidate.AssetLinks)
            .FirstOrDefaultAsync(candidate => candidate.Id == liabilityId, cancellationToken);
        if (liability is null) return null;
        await ValidateAsync(liability.ScenarioId, request, cancellationToken);

        ApplyCore(liability, request);
        ApplyAssetAllocations(liability, request.AssetAllocations ?? []);
        await db.SaveChangesAsync(cancellationToken);
        return liability;
    }

    /// <summary>Rejects invalid percentages, currencies, duplicate links, and cross-scenario assets.</summary>
    private async Task ValidateAsync(Guid scenarioId, LiabilityRequest request, CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name)) errors["name"] = ["A liability name is required."];
        if (request.OutstandingBalance < 0 || request.MonthlyPayment < 0) errors["amounts"] = ["Debt amounts cannot be negative."];
        if (request.ResponsibilityRate is <= 0 or > 1) errors["responsibilityRate"] = ["Your debt share must be greater than 0% and at most 100%."];
        if (request.Currency.Trim().Length != 3 || !request.Currency.Trim().All(char.IsAsciiLetter)) errors["currency"] = ["Use a three-letter ISO 4217 currency code."];

        var allocations = request.AssetAllocations ?? [];
        if (allocations.Select(allocation => allocation.AssetId).Distinct().Count() != allocations.Count)
            errors["assetAllocations"] = ["An asset can only be linked once to the same liability."];
        if (allocations.Any(allocation => allocation.AllocationRate is <= 0 or > 1) || allocations.Sum(allocation => allocation.AllocationRate) > 1m)
            errors["assetAllocations"] = ["The financed shares must be greater than 0% and cannot exceed 100% in total."];

        var assetIds = allocations.Select(allocation => allocation.AssetId).ToArray();
        if (assetIds.Length > 0)
        {
            var validCount = await db.Assets.AsNoTracking().CountAsync(asset => asset.ScenarioId == scenarioId && assetIds.Contains(asset.Id), cancellationToken);
            if (validCount != assetIds.Length) errors["assetAllocations"] = ["Every financed asset must belong to the same scenario."];
        }
        if (errors.Count > 0) throw new LiabilityValidationException(errors);
    }

    /// <summary>Copies the strongly typed debt facts while preserving server-owned identity and scenario fields.</summary>
    private static void ApplyCore(Liability liability, LiabilityRequest request)
    {
        liability.Name = request.Name.Trim();
        liability.Kind = request.Kind;
        liability.OutstandingBalance = request.OutstandingBalance;
        liability.ResponsibilityRate = request.ResponsibilityRate;
        liability.InterestRate = request.InterestRate;
        liability.MonthlyPayment = request.MonthlyPayment;
        liability.PaidOffOn = request.PaidOffOn;
        liability.Currency = request.Currency.Trim().ToUpperInvariant();
    }

    /// <summary>Reconciles relationship rows so debt editing cannot leave stale financed assets behind.</summary>
    private void ApplyAssetAllocations(Liability liability, IReadOnlyList<LiabilityAssetAllocationRequest> allocations)
    {
        var obsolete = liability.AssetLinks.ToDictionary(link => link.AssetId);
        foreach (var allocation in allocations)
        {
            if (obsolete.Remove(allocation.AssetId, out var existing))
                existing.AllocationRate = allocation.AllocationRate;
            else
                liability.AssetLinks.Add(new AssetLiabilityLink { LiabilityId = liability.Id, AssetId = allocation.AssetId, AllocationRate = allocation.AllocationRate });
        }
        db.AssetLiabilityLinks.RemoveRange(obsolete.Values);
    }
}
