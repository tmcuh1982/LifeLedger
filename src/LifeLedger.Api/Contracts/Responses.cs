using LifeLedger.Api.Domain;

namespace LifeLedger.Api.Contracts;

public sealed record CountryInfo(string Code, string Name, decimal DefaultInflation, int DefaultRetirementAge, string Currency);
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

public sealed record ProjectionYear(
    int Year,
    int Age,
    decimal NetWorth,
    decimal CashFlow,
    decimal Income,
    decimal Expenses,
    decimal PassiveIncome,
    decimal InflationAdjustedNetWorth);
public sealed record AllocationSlice(string Name, AssetKind Kind, decimal Value, decimal Percentage);
public sealed record SimulationResponse(SimulationMode Mode, int Runs, decimal ProbabilityOfSuccess, IReadOnlyList<ProjectionYear> Timeline, IReadOnlyList<decimal> TerminalNetWorths, IReadOnlyList<string> Warnings);
public sealed record ScenarioDetail(FinancialScenario Scenario, Profile Profile);
