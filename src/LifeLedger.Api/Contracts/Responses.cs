using LifeLedger.Api.Domain;

namespace LifeLedger.Api.Contracts;

/// <summary>Provides the default assumptions available for a supported country.</summary>
public sealed record CountryInfo(string Code, string Name, decimal DefaultInflation, int DefaultRetirementAge, string Currency);

/// <summary>Summarises the current and projected state of a financial scenario.</summary>
public sealed record DashboardResponse(
    Guid ScenarioId,
    string ScenarioName,
    string Currency,
    decimal CurrentNetWorth,
    decimal FutureNetWorth,
    decimal PassiveMonthlyIncome,
    decimal EstimatedRetirementIncome,
    DateOnly? FinancialIndependenceDate,
    decimal InflationAdjustedPurchasingPowerChange,
    decimal ProbabilityOfSuccess,
    IReadOnlyList<ProjectionYear> Timeline,
    IReadOnlyList<AllocationSlice> Allocation,
    IReadOnlyList<string> Warnings);

/// <summary>Stores the calculated financial values for one projected calendar year.</summary>
public sealed record ProjectionYear(
    int Year,
    int Age,
    decimal NetWorth,
    decimal CashFlow,
    decimal Income,
    decimal Expenses,
    decimal PassiveIncome,
    decimal InflationAdjustedNetWorth);
/// <summary>Represents one asset group in the portfolio allocation.</summary>
public sealed record AllocationSlice(string Name, AssetKind Kind, decimal Value, decimal Percentage);

/// <summary>Returns the result of a deterministic, historical, or Monte Carlo simulation.</summary>
public sealed record SimulationResponse(SimulationMode Mode, int Runs, decimal ProbabilityOfSuccess, IReadOnlyList<ProjectionYear> Timeline, IReadOnlyList<decimal> TerminalNetWorths, IReadOnlyList<string> Warnings);

/// <summary>Returns a scenario with the profile required to interpret it.</summary>
public sealed record ScenarioDetail(FinancialScenario Scenario, Profile Profile);
