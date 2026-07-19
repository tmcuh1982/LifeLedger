using LifeLedger.Api.Contracts;
using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Creates and maintains dated, user-controlled allocation-strategy versions.</summary>
public interface IAllocationStrategyService
{
    /// <summary>Lists all strategy versions for a scenario, newest effective version first.</summary>
    Task<IReadOnlyList<AllocationStrategy>> ListAsync(Guid scenarioId, CancellationToken cancellationToken);
    /// <summary>Creates a future or current allocation-strategy version.</summary>
    Task<AllocationStrategy> CreateAsync(Guid scenarioId, AllocationStrategyRequest request, CancellationToken cancellationToken);
    /// <summary>Replaces one strategy version and its complete target-band list.</summary>
    Task<AllocationStrategy?> UpdateAsync(Guid strategyId, AllocationStrategyRequest request, CancellationToken cancellationToken);
    /// <summary>Deletes one strategy version without changing assets or historical valuations.</summary>
    Task<bool> DeleteAsync(Guid strategyId, CancellationToken cancellationToken);
}

/// <summary>EF Core implementation that makes active strategy versions unambiguous and target lists internally consistent.</summary>
public sealed class AllocationStrategyService(LifeLedgerDbContext db) : IAllocationStrategyService
{
    /// <inheritdoc />
    public async Task<IReadOnlyList<AllocationStrategy>> ListAsync(Guid scenarioId, CancellationToken cancellationToken) =>
        await db.AllocationStrategies.AsNoTracking().Include(strategy => strategy.Targets)
            .Where(strategy => strategy.ScenarioId == scenarioId)
            .OrderByDescending(strategy => strategy.EffectiveFrom).ThenBy(strategy => strategy.Name)
            .ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<AllocationStrategy> CreateAsync(Guid scenarioId, AllocationStrategyRequest request, CancellationToken cancellationToken)
    {
        if (!await db.Scenarios.AnyAsync(scenario => scenario.Id == scenarioId, cancellationToken)) throw new KeyNotFoundException("The scenario does not exist.");
        Validate(request);
        await EnsureDoesNotOverlapAsync(scenarioId, null, request.EffectiveFrom, request.EffectiveTo, cancellationToken);
        var strategy = new AllocationStrategy { ScenarioId = scenarioId };
        Apply(strategy, request);
        db.AllocationStrategies.Add(strategy);
        await db.SaveChangesAsync(cancellationToken);
        return strategy;
    }

    /// <inheritdoc />
    public async Task<AllocationStrategy?> UpdateAsync(Guid strategyId, AllocationStrategyRequest request, CancellationToken cancellationToken)
    {
        var strategy = await db.AllocationStrategies.Include(candidate => candidate.Targets).SingleOrDefaultAsync(candidate => candidate.Id == strategyId, cancellationToken);
        if (strategy is null) return null;
        Validate(request);
        await EnsureDoesNotOverlapAsync(strategy.ScenarioId, strategy.Id, request.EffectiveFrom, request.EffectiveTo, cancellationToken);
        Apply(strategy, request);
        await db.SaveChangesAsync(cancellationToken);
        return strategy;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(Guid strategyId, CancellationToken cancellationToken)
    {
        var strategy = await db.AllocationStrategies.FindAsync([strategyId], cancellationToken);
        if (strategy is null) return false;
        db.AllocationStrategies.Remove(strategy);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Validates target names, percentages, and the optional unallocated remainder without prescribing an investment thesis.</summary>
    private static void Validate(AllocationStrategyRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 100) throw new ArgumentException("A strategy name between 1 and 100 characters is required.", nameof(request));
        if (request.Description?.Trim().Length > 1_000) throw new ArgumentException("A strategy description cannot exceed 1,000 characters.", nameof(request));
        if (request.EffectiveTo is { } effectiveTo && effectiveTo < request.EffectiveFrom) throw new ArgumentException("The strategy end date cannot precede its effective date.", nameof(request));
        var targets = request.Targets ?? [];
        if (targets.Count == 0) throw new ArgumentException("A strategy needs at least one allocation target.", nameof(request));
        if (targets.Any(target => string.IsNullOrWhiteSpace(target.Category) || target.Category.Trim().Length > 80)) throw new ArgumentException("Each target needs a category of at most 80 characters.", nameof(request));
        if (targets.Select(target => target.Category.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != targets.Count) throw new ArgumentException("A strategy can include each category only once.", nameof(request));
        if (targets.Any(target => target.TargetPercentage is < 0m or > 100m || target.TolerancePercentage is < 0m or > 100m)) throw new ArgumentException("Target and tolerance percentages must be between 0 and 100.", nameof(request));
        if (targets.Sum(target => target.TargetPercentage) > 100m) throw new ArgumentException("The total target allocation cannot exceed 100%.", nameof(request));
    }

    /// <summary>Prevents two strategy versions from claiming authority over the same calendar day.</summary>
    private async Task EnsureDoesNotOverlapAsync(Guid scenarioId, Guid? excludedStrategyId, DateOnly from, DateOnly? to, CancellationToken cancellationToken)
    {
        var existing = await db.AllocationStrategies.AsNoTracking()
            .Where(strategy => strategy.ScenarioId == scenarioId && (excludedStrategyId == null || strategy.Id != excludedStrategyId))
            .Select(strategy => new { strategy.EffectiveFrom, strategy.EffectiveTo })
            .ToListAsync(cancellationToken);
        if (existing.Any(strategy => strategy.EffectiveFrom <= (to ?? DateOnly.MaxValue) && (strategy.EffectiveTo ?? DateOnly.MaxValue) >= from))
            throw new InvalidOperationException("This strategy overlaps an existing strategy version. Close the earlier version or choose another effective date.");
    }

    /// <summary>Replaces all editable fields so a saved strategy exactly represents the user's current version.</summary>
    private static void Apply(AllocationStrategy strategy, AllocationStrategyRequest request)
    {
        strategy.Name = request.Name.Trim();
        strategy.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        strategy.EffectiveFrom = request.EffectiveFrom;
        strategy.EffectiveTo = request.EffectiveTo;
        strategy.Targets = (request.Targets ?? []).Select(target => new AllocationStrategyTarget
        {
            Category = target.Category.Trim(),
            TargetPercentage = target.TargetPercentage,
            TolerancePercentage = target.TolerancePercentage
        }).ToList();
    }
}
