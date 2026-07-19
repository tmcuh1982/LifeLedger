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
    decimal ExpectedMonthlyPortfolioGrowth,
    decimal EstimatedRetirementIncome,
    DateOnly? FinancialIndependenceDate,
    decimal InflationAdjustedPurchasingPowerChange,
    decimal ProbabilityOfSuccess,
    IReadOnlyList<ProjectionYear> Timeline,
    IReadOnlyList<AllocationSlice> Allocation,
    AllocationStrategyAssessment? AllocationStrategy,
    IReadOnlyList<SimulationWarning> Warnings);

/// <summary>Describes one language-neutral risk signal with an optional numeric value for client-side localisation.</summary>
public sealed record SimulationWarning(string Code, decimal? Value = null);

/// <summary>Stores the calculated financial values for one projected calendar year.</summary>
public sealed record ProjectionYear(
    int Year,
    int Age,
    decimal NetWorth,
    decimal CashFlow,
    decimal Income,
    decimal Expenses,
    decimal PassiveIncome,
    decimal InflationAdjustedNetWorth,
    decimal PlannedExpenseSavings,
    decimal PlannedExpenseFundBalance,
    IReadOnlyList<ProjectionWealthComponent> WealthComponents,
    IReadOnlyList<ProjectedAssetSale> AssetSales);

/// <summary>Explains the complete financial breakdown of one asset sale applied during a projected year.</summary>
public sealed record ProjectedAssetSale(
    Guid SaleId,
    Guid AssetId,
    string Name,
    DateOnly HappensOn,
    decimal GrossProceeds,
    decimal SellingCosts,
    decimal CapitalGainsTax,
    decimal DebtRepaid,
    decimal NetProceeds,
    string Currency,
    AssetSaleDestination Destination);

/// <summary>Identifies how one projected wealth component participates in the household balance sheet.</summary>
public enum ProjectionWealthComponentType
{
    /// <summary>A currently owned asset grouped by its user-facing category.</summary>
    Asset,
    /// <summary>Capital accumulated through configured monthly investment plans.</summary>
    Investment,
    /// <summary>Future cash surpluses or deficits that are not assigned to a specific owned asset.</summary>
    ProjectedCash,
    /// <summary>Money reserved for exceptional expenses that are being funded in advance.</summary>
    PlannedExpenseReserve,
    /// <summary>Outstanding debt, represented as a negative contribution to net worth.</summary>
    Liability
}

/// <summary>Stores one category-level contribution to projected net worth for a single timeline point.</summary>
public sealed record ProjectionWealthComponent(
    string Key,
    string Category,
    AssetKind? Kind,
    ProjectionWealthComponentType Type,
    decimal Value);
/// <summary>Represents one asset group in the portfolio allocation.</summary>
public sealed record AllocationSlice(string Name, AssetKind Kind, decimal Value, decimal Percentage);

/// <summary>Compares the active target-allocation strategy with the current, category-level investable portfolio.</summary>
public sealed record AllocationStrategyAssessment(
    string Name,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    decimal TotalTargetPercentage,
    IReadOnlyList<AllocationTargetAssessment> Targets);

/// <summary>Reports one target category's current share and whether it is inside its user-defined range.</summary>
public sealed record AllocationTargetAssessment(
    string Category,
    decimal TargetPercentage,
    decimal TolerancePercentage,
    decimal ActualPercentage,
    decimal DifferencePercentage,
    AllocationTargetState State);

/// <summary>Indicates whether a category is on target, underweight, or overweight relative to its allowed band.</summary>
public enum AllocationTargetState { WithinRange, Underweight, Overweight }

/// <summary>Represents one locally stored observation in the actual net-worth history.</summary>
public sealed record NetWorthSnapshotResponse(DateTimeOffset CapturedAt, decimal NetWorth, string Currency);

/// <summary>Returns the result of a deterministic, historical, or Monte Carlo simulation.</summary>
public sealed record SimulationResponse(SimulationMode Mode, int Runs, decimal ProbabilityOfSuccess, IReadOnlyList<ProjectionYear> Timeline, IReadOnlyList<decimal> TerminalNetWorths, IReadOnlyList<SimulationWarning> Warnings);

/// <summary>Describes a user-defined asset category and how many assets currently use it.</summary>
public sealed record AssetCategoryResponse(string Name, int AssetCount);

/// <summary>Supported storage and editor type for one dynamic asset-profile field.</summary>
public enum AssetProfileFieldType
{
    /// <summary>Free-form short text.</summary>
    Text,
    /// <summary>Numeric value without a specialised unit.</summary>
    Number,
    /// <summary>Calendar date.</summary>
    Date,
    /// <summary>Yes or no value.</summary>
    Boolean,
    /// <summary>Closed choice from a definition-owned option list.</summary>
    Select,
    /// <summary>Surface area in square metres.</summary>
    Area,
    /// <summary>Distance in kilometres.</summary>
    Distance,
    /// <summary>Simple condition score from one to five.</summary>
    Condition
}

/// <summary>Defines one translated choice offered by a select profile field.</summary>
public sealed record AssetProfileOptionDefinition(string Value, IReadOnlyDictionary<string, string> Labels);

/// <summary>Defines one strongly typed field in a versioned asset characteristic profile.</summary>
public sealed record AssetProfileFieldDefinition(
    string Key,
    IReadOnlyDictionary<string, string> Labels,
    AssetProfileFieldType Type,
    bool Required = false,
    IReadOnlyList<AssetProfileOptionDefinition>? Options = null);

/// <summary>Describes a stable, versioned asset profile that a client can render dynamically.</summary>
public sealed record AssetProfileDefinition(
    string Key,
    int Version,
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<AssetProfileFieldDefinition> Fields,
    bool IsCustom = false);

/// <summary>Contains calculated performance values for one asset in its own currency.</summary>
public sealed record AssetPerformanceResponse(
    string Currency,
    decimal AcquisitionBasis,
    decimal GrossGain,
    decimal? GainRate,
    decimal LinkedDebt,
    decimal NetEquity);

/// <summary>Returns the persisted asset together with its derived financial performance.</summary>
public sealed record AssetDossierResponse(Asset Asset, AssetPerformanceResponse Performance);

/// <summary>Returns a scenario with the profile required to interpret it.</summary>
public sealed record ScenarioDetail(FinancialScenario Scenario, Profile Profile);
