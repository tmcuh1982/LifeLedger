using LifeLedger.Api.Data;
using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Services;

public interface IScenarioRepository
{
    Task<FinancialScenario?> GetAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class ScenarioRepository(LifeLedgerDbContext db) : IScenarioRepository
{
    public Task<FinancialScenario?> GetAsync(Guid id, CancellationToken cancellationToken) =>
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
