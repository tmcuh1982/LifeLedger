using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

/// <summary>Loads a complete financial scenario aggregate for read and simulation operations.</summary>
public interface IScenarioRepository
{
    /// <summary>Returns a scenario and its required related data, or null when it does not exist.</summary>
    Task<FinancialScenario?> GetAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>EF Core implementation that loads the scenario graph without N+1 queries.</summary>
public sealed class ScenarioRepository(LifeLedgerDbContext db) : IScenarioRepository
{
    /// <inheritdoc />
    public Task<FinancialScenario?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        // Split queries avoid an explosive cartesian product across independent collections.
        db.Scenarios
            .AsSplitQuery()
            .Include(x => x.Profile).ThenInclude(x => x!.Careers)
            .Include(x => x.Assumptions)
            .Include(x => x.Incomes)
            .Include(x => x.Assets)
            .Include(x => x.Liabilities)
            .Include(x => x.Expenses)
            .Include(x => x.Investments)
            .Include(x => x.Events)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
}
