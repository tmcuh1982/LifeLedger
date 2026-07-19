using System.Text.Json;
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

/// <summary>Supplies the editable name of a user-defined asset category.</summary>
public sealed record AssetCategoryNameRequest(string Name);

/// <summary>Allocates a fraction of one liability's current balance to an asset.</summary>
public sealed record AssetLiabilityAllocationRequest(Guid LiabilityId, decimal AllocationRate);

/// <summary>Allocates a fraction of one liability to an asset that it finances.</summary>
public sealed record LiabilityAssetAllocationRequest(Guid AssetId, decimal AllocationRate);

/// <summary>Supplies editable debt facts together with the assets financed by that debt.</summary>
public sealed record LiabilityRequest(
    string Name,
    LiabilityKind Kind,
    decimal OutstandingBalance,
    decimal ResponsibilityRate,
    decimal InterestRate,
    decimal MonthlyPayment,
    DateOnly? PaidOffOn,
    string Currency,
    IReadOnlyList<LiabilityAssetAllocationRequest> AssetAllocations);

/// <summary>Supplies the complete financial and characteristic dossier of an asset in one transaction.</summary>
public sealed record AssetDossierRequest(
    string Name,
    AssetKind Kind,
    string? CustomCategory,
    decimal CurrentValue,
    decimal ExpectedAnnualReturn,
    decimal Volatility,
    bool IsLiquid,
    string? Ticker,
    decimal Quantity,
    decimal CapitalGainsTaxRate,
    string? CapitalGainsTaxCountryCode,
    string Currency,
    decimal PurchasePrice,
    decimal AcquisitionCosts,
    DateOnly? PurchasedOn,
    DateOnly? ValuedOn,
    string? ValuationSource,
    string? ProfileDefinitionKey,
    int? ProfileDefinitionVersion,
    IReadOnlyDictionary<string, JsonElement>? ProfileValues,
    IReadOnlyList<AssetLiabilityAllocationRequest> LiabilityAllocations,
    bool IsIncludedInPortfolioAllocation = true,
    decimal OwnershipRate = 1m);

/// <summary>Supplies the translated name and fields of a user-defined asset characteristic profile.</summary>
public sealed record AssetProfileDefinitionRequest(
    IReadOnlyDictionary<string, string> Labels,
    IReadOnlyList<AssetProfileFieldDefinition> Fields);

/// <summary>Configures a local, read-only IBKR Flex Web Service connection for one scenario.</summary>
public sealed record IbkrFlexConfigurationRequest(string AccessToken, long ActivityQueryId);

/// <summary>Sets one allocation category's desired share and permitted deviation in percentage points.</summary>
public sealed record AllocationStrategyTargetRequest(string Category, decimal TargetPercentage, decimal TolerancePercentage);

/// <summary>Supplies one dated, editable version of a scenario's long-term allocation strategy.</summary>
public sealed record AllocationStrategyRequest(string Name, string? Description, DateOnly EffectiveFrom, DateOnly? EffectiveTo, IReadOnlyList<AllocationStrategyTargetRequest> Targets);

/// <summary>Supplies every editable assumption for one explicit future asset sale.</summary>
public sealed record PlannedAssetSaleRequest(
    string Name,
    Guid AssetId,
    DateOnly HappensOn,
    bool UseProjectedValue,
    decimal GrossSalePrice,
    decimal SellingCosts,
    decimal CapitalGainsTaxRate,
    string? CapitalGainsTaxCountryCode,
    bool RepayLinkedLiabilities,
    AssetSaleDestination Destination,
    Guid? DestinationAssetId,
    Guid? DestinationInvestmentPlanId,
    string Currency,
    string? Notes);

/// <summary>Portable, versioned representation of a profile, scenarios, and custom asset-sheet schemas.</summary>
public sealed record LifeLedgerExport(
    int SchemaVersion,
    DateTimeOffset ExportedAt,
    Profile Profile,
    IReadOnlyList<FinancialScenario> Scenarios,
    IReadOnlyList<AssetProfileDefinition>? AssetProfileDefinitions = null);
