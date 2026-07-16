using LifeLedger.Api.Domain;

namespace LifeLedger.Api.Contracts;

public sealed record CreateScenarioRequest(Guid ProfileId, string Name, string? Description, Guid? ParentScenarioId = null);
public sealed record UpdateScenarioRequest(string Name, string Description, bool IsBaseline, DateOnly StartsOn, SimulationAssumptionsRequest Assumptions);
public sealed record SimulationAssumptionsRequest(decimal InflationRate, decimal SalaryGrowthRate, decimal SafeWithdrawalRate, int RetirementAge, int MonteCarloRuns, decimal DefaultReturnVolatility);
public sealed record SimulationRequest(SimulationMode Mode = SimulationMode.Deterministic, int? Years = null, int? Runs = null);
public sealed record ImportRequest(LifeLedgerExport Document, bool ReplaceExisting = false);

public sealed record LifeLedgerExport(int SchemaVersion, DateTimeOffset ExportedAt, Profile Profile, IReadOnlyList<FinancialScenario> Scenarios);
