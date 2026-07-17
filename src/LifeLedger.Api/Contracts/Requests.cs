using LifeLedger.Api.Domain;

namespace LifeLedger.Api.Contracts;

/// <summary>Requests creation of a scenario for a profile.</summary>
public sealed record CreateScenarioRequest(Guid ProfileId, string Name, string? Description, Guid? ParentScenarioId = null);

/// <summary>Supplies editable scenario metadata and simulation assumptions.</summary>
public sealed record UpdateScenarioRequest(string Name, string Description, bool IsBaseline, DateOnly StartsOn, SimulationAssumptionsRequest Assumptions);

/// <summary>Supplies user-configurable financial assumptions for a projection.</summary>
public sealed record SimulationAssumptionsRequest(decimal InflationRate, decimal SalaryGrowthRate, decimal SafeWithdrawalRate, int RetirementAge, int MonteCarloRuns, decimal DefaultReturnVolatility);

/// <summary>Selects a simulation method and optionally overrides its duration or number of runs.</summary>
public sealed record SimulationRequest(SimulationMode Mode = SimulationMode.Deterministic, int? Years = null, int? Runs = null);

/// <summary>Requests import of a previously exported financial plan.</summary>
public sealed record ImportRequest(LifeLedgerExport Document, bool ReplaceExisting = false);

/// <summary>Supplies CSV text from a bank or Revolut export for local expense analysis.</summary>
public sealed record CsvExpenseImportRequest(string Csv);

/// <summary>Supplies the editable name of a user-defined asset category.</summary>
public sealed record AssetCategoryNameRequest(string Name);

/// <summary>Portable, versioned representation of a profile and its scenarios.</summary>
public sealed record LifeLedgerExport(int SchemaVersion, DateTimeOffset ExportedAt, Profile Profile, IReadOnlyList<FinancialScenario> Scenarios);
