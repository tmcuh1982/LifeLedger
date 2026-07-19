namespace LifeLedger.Api.Domain;

/// <summary>Aggregates every financial input required to project one possible future.</summary>
public sealed class FinancialScenario
{
    /// <summary>Stable identifier of the scenario.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the profile that owns this scenario.</summary>
    public Guid ProfileId { get; set; }
    /// <summary>Navigation to the owning profile.</summary>
    public Profile? Profile { get; set; }
    /// <summary>Optional source scenario from which this scenario was copied.</summary>
    public Guid? ParentScenarioId { get; set; }
    /// <summary>Human-readable name of the scenario.</summary>
    public string Name { get; set; } = "Base plan";
    /// <summary>Optional explanation of the hypothesis represented by the scenario.</summary>
    public string Description { get; set; } = string.Empty;
    /// <summary>Whether this is the profile's reference scenario.</summary>
    public bool IsBaseline { get; set; }
    /// <summary>Date from which projections begin.</summary>
    public DateOnly StartsOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    /// <summary>UTC timestamp of the last scenario update.</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Configurable assumptions used by the projection engine.</summary>
    public SimulationAssumptions Assumptions { get; set; } = new();
    /// <summary>Declared income streams.</summary>
    public List<IncomeStream> Incomes { get; set; } = [];
    /// <summary>Declared assets and their market assumptions.</summary>
    public List<Asset> Assets { get; set; } = [];
    /// <summary>Outstanding debts and other liabilities.</summary>
    public List<Liability> Liabilities { get; set; } = [];
    /// <summary>Recurring and exceptional expenses.</summary>
    public List<Expense> Expenses { get; set; } = [];
    /// <summary>Regular investment contributions.</summary>
    public List<InvestmentPlan> Investments { get; set; } = [];
    /// <summary>Explicit future sales that transfer an owned asset into cash or another investment.</summary>
    public List<PlannedAssetSale> AssetSales { get; set; } = [];
    /// <summary>Life events that change cash flow or net worth.</summary>
    public List<ScenarioEvent> Events { get; set; } = [];
    /// <summary>Observed bank accounts and transaction history, which do not alter projections automatically.</summary>
    public List<BankAccount> BankAccounts { get; set; } = [];
    /// <summary>Versioned target allocations that define the scenario's long-term portfolio strategy.</summary>
    public List<AllocationStrategy> AllocationStrategies { get; set; } = [];
}

/// <summary>Stores the financial assumptions that influence a scenario projection.</summary>
public sealed class SimulationAssumptions
{
    /// <summary>Stable identifier of these assumptions.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the owning scenario.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the owning scenario.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>Expected annual headline inflation expressed as a fraction.</summary>
    public decimal InflationRate { get; set; } = 0.025m;
    /// <summary>Expected annual salary growth above or alongside inflation, as a fraction.</summary>
    public decimal SalaryGrowthRate { get; set; } = 0.02m;
    /// <summary>Annual share of the portfolio assumed withdrawable in retirement.</summary>
    public decimal SafeWithdrawalRate { get; set; } = 0.04m;
    /// <summary>Age at which salaried income stops in the projection.</summary>
    public int RetirementAge { get; set; } = 65;
    /// <summary>Number of simulated paths for Monte Carlo mode.</summary>
    public int MonteCarloRuns { get; set; } = 500;
    /// <summary>Fallback annual return volatility for assets without a specific value.</summary>
    public decimal DefaultReturnVolatility { get; set; } = 0.12m;
}

/// <summary>Represents an income stream received during a date range.</summary>
public sealed class IncomeStream
{
    /// <summary>Stable identifier of the income stream.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the owning scenario.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the owning scenario.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>Name shown to the user.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Business category of the income.</summary>
    public IncomeKind Kind { get; set; }
    /// <summary>Gross amount received per month in <see cref="Currency"/>.</summary>
    public decimal MonthlyAmount { get; set; }
    /// <summary>How the gross income amount is entered and distributed through the year.</summary>
    public IncomeAmountMode AmountMode { get; set; } = IncomeAmountMode.Monthly;
    /// <summary>Gross amount expected for a complete year when <see cref="AmountMode"/> is not monthly.</summary>
    public decimal AnnualAmount { get; set; }
    /// <summary>Expected annual growth as a fraction.</summary>
    public decimal AnnualGrowthRate { get; set; }
    /// <summary>First date on which the income is active.</summary>
    public DateOnly StartsOn { get; set; }
    /// <summary>Last active date; null means it continues indefinitely.</summary>
    public DateOnly? EndsOn { get; set; }
    /// <summary>Whether the projection applies <see cref="TaxRate"/> to this income.</summary>
    public bool IsTaxable { get; set; } = true;
    /// <summary>Estimated effective tax rate, expressed as a fraction.</summary>
    public decimal TaxRate { get; set; }
    /// <summary>Optional country code that explains the tax assumption.</summary>
    public string? TaxCountryCode { get; set; }
    /// <summary>ISO 4217 currency of the monthly amount.</summary>
    public string Currency { get; set; } = "EUR";
    /// <summary>Optional asset that generates this income, such as a rented apartment.</summary>
    public Guid? LinkedAssetId { get; set; }
    /// <summary>Navigation to the asset that generates this income.</summary>
    public Asset? LinkedAsset { get; set; }
    /// <summary>Optional calendar-month shares used for seasonal annual income.</summary>
    public List<IncomeMonthlyAllocation> MonthlyAllocations { get; set; } = [];
}

/// <summary>Allocates a share of one seasonal annual income to a calendar month.</summary>
public sealed class IncomeMonthlyAllocation
{
    /// <summary>Identifier of the income stream that owns this allocation.</summary>
    public Guid IncomeStreamId { get; set; }
    /// <summary>Navigation to the owning income stream.</summary>
    public IncomeStream? IncomeStream { get; set; }
    /// <summary>Calendar month number from 1 for January to 12 for December.</summary>
    public int Month { get; set; }
    /// <summary>Share of the annual amount assigned to this month, expressed as a non-negative fraction.</summary>
    public decimal Share { get; set; }
}

/// <summary>Represents an owned asset and its return, volatility, and tax assumptions.</summary>
public sealed class Asset
{
    /// <summary>Stable identifier of the asset.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the owning scenario.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the owning scenario.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>Name shown to the user.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Business category of the asset.</summary>
    public AssetKind Kind { get; set; }
    /// <summary>Optional user-defined display category; <see cref="Kind"/> remains the technical calculation type.</summary>
    public string? CustomCategory { get; set; }
    /// <summary>Current market value in <see cref="Currency"/>.</summary>
    public decimal CurrentValue { get; set; }
    /// <summary>Share of the complete asset legally or economically owned by the profile, between zero and one.</summary>
    public decimal OwnershipRate { get; set; } = 1m;
    /// <summary>Price paid for the asset in <see cref="Currency"/>; zero means it is not known.</summary>
    public decimal PurchasePrice { get; set; }
    /// <summary>Acquisition fees and taxes paid in <see cref="Currency"/>.</summary>
    public decimal AcquisitionCosts { get; set; }
    /// <summary>Date on which the asset was acquired, when known.</summary>
    public DateOnly? PurchasedOn { get; set; }
    /// <summary>Date on which <see cref="CurrentValue"/> was last estimated.</summary>
    public DateOnly? ValuedOn { get; set; }
    /// <summary>User-facing explanation of the current valuation source.</summary>
    public string? ValuationSource { get; set; }
    /// <summary>Expected annual gross return as a fraction.</summary>
    public decimal ExpectedAnnualReturn { get; set; }
    /// <summary>Expected annual price volatility as a fraction.</summary>
    public decimal Volatility { get; set; }
    /// <summary>Whether the asset can reasonably fund short-term spending.</summary>
    public bool IsLiquid { get; set; } = true;
    /// <summary>Optional public market ticker for an ETF or stock.</summary>
    public string? Ticker { get; set; }
    /// <summary>External system that owns the stable instrument identifier, such as <c>IBKR Flex</c>.</summary>
    public string? ExternalProvider { get; set; }
    /// <summary>Stable identifier assigned by <see cref="ExternalProvider"/> for idempotent imports.</summary>
    public string? ExternalId { get; set; }
    /// <summary>Number of market units held when a ticker is provided.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Whether this asset participates in the investable-portfolio allocation and its target strategy.</summary>
    public bool IsIncludedInPortfolioAllocation { get; set; } = true;
    /// <summary>Estimated effective tax rate applied to annual capital gains.</summary>
    public decimal CapitalGainsTaxRate { get; set; }
    /// <summary>Optional country code that explains the capital-gains assumption.</summary>
    public string? CapitalGainsTaxCountryCode { get; set; }
    /// <summary>ISO 4217 currency of the current value.</summary>
    public string Currency { get; set; } = "EUR";
    /// <summary>Optional versioned characteristic sheet attached to this asset.</summary>
    public AssetCharacteristicProfile? CharacteristicProfile { get; set; }
    /// <summary>Debt allocations used to calculate the asset's current net equity.</summary>
    public List<AssetLiabilityLink> LiabilityLinks { get; set; } = [];
    /// <summary>Daily valuation observations used to explain how this asset changed over time.</summary>
    public List<AssetValuationSnapshot> ValuationSnapshots { get; set; } = [];
}

/// <summary>Represents one dated, user-owned version of a target portfolio-allocation strategy.</summary>
public sealed class AllocationStrategy
{
    /// <summary>Stable identifier of this strategy version.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the scenario that owns this strategy version.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the scenario that owns this strategy version.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>User-facing name, such as <c>Balanced growth 2026</c>.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Optional rationale that records the investor's thesis without changing calculations.</summary>
    public string? Description { get; set; }
    /// <summary>First calendar date on which this strategy version is active.</summary>
    public DateOnly EffectiveFrom { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    /// <summary>Last calendar date on which this strategy version is active; null means it remains current.</summary>
    public DateOnly? EffectiveTo { get; set; }
    /// <summary>Target bands assigned to allocation categories for this strategy version.</summary>
    public List<AllocationStrategyTarget> Targets { get; set; } = [];
}

/// <summary>Defines one category's target portfolio share and allowed deviation within a strategy version.</summary>
public sealed class AllocationStrategyTarget
{
    /// <summary>Stable identifier of this target row.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the strategy version that owns this target.</summary>
    public Guid AllocationStrategyId { get; set; }
    /// <summary>Navigation to the owning strategy version.</summary>
    public AllocationStrategy? AllocationStrategy { get; set; }
    /// <summary>Allocation category label shared by assets across all brokers, such as <c>ETF World</c>.</summary>
    public string Category { get; set; } = string.Empty;
    /// <summary>Desired portfolio share expressed as a percentage from zero to one hundred.</summary>
    public decimal TargetPercentage { get; set; }
    /// <summary>Allowed absolute deviation, in percentage points, on either side of the target.</summary>
    public decimal TolerancePercentage { get; set; }
}

/// <summary>Stores the schema identifier and values of one versioned asset characteristic sheet.</summary>
public sealed class AssetCharacteristicProfile
{
    /// <summary>Identifier of the asset that owns this one-to-one profile.</summary>
    public Guid AssetId { get; set; }
    /// <summary>Navigation to the owning asset.</summary>
    public Asset? Asset { get; set; }
    /// <summary>Stable key of the profile definition, such as <c>home</c> or <c>vehicle</c>.</summary>
    public string DefinitionKey { get; set; } = string.Empty;
    /// <summary>Definition version used to interpret <see cref="ValuesJson"/>.</summary>
    public int DefinitionVersion { get; set; } = 1;
    /// <summary>Validated characteristic values stored as a JSON object.</summary>
    public string ValuesJson { get; set; } = "{}";
}

/// <summary>Allocates a share of one liability to one asset without duplicating the debt balance.</summary>
public sealed class AssetLiabilityLink
{
    /// <summary>Identifier of the financed asset.</summary>
    public Guid AssetId { get; set; }
    /// <summary>Navigation to the financed asset.</summary>
    public Asset? Asset { get; set; }
    /// <summary>Identifier of the allocated liability.</summary>
    public Guid LiabilityId { get; set; }
    /// <summary>Navigation to the allocated liability.</summary>
    public Liability? Liability { get; set; }
    /// <summary>Share of the current outstanding balance allocated to this asset, between zero and one.</summary>
    public decimal AllocationRate { get; set; } = 1m;
}

/// <summary>Stores one locally recorded market price for a tracked asset.</summary>
public sealed class AssetQuoteSnapshot
{
    /// <summary>Stable identifier of the snapshot.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the quoted asset.</summary>
    public Guid AssetId { get; set; }
    /// <summary>Navigation to the quoted asset.</summary>
    public Asset? Asset { get; set; }
    /// <summary>UTC timestamp at which the price was captured.</summary>
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Quoted unit price.</summary>
    public decimal Price { get; set; }
    /// <summary>ISO 4217 currency of the quoted price.</summary>
    public string Currency { get; set; } = "EUR";
    /// <summary>Name of the provider that supplied the quote.</summary>
    public string Source { get; set; } = "Yahoo Finance";
}

/// <summary>Stores one locally observed total value for an asset on a calendar date.</summary>
public sealed class AssetValuationSnapshot
{
    /// <summary>Stable identifier of the valuation point.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the valued asset.</summary>
    public Guid AssetId { get; set; }
    /// <summary>Navigation to the valued asset.</summary>
    public Asset? Asset { get; set; }
    /// <summary>Calendar date represented by this point.</summary>
    public DateOnly ValuedOn { get; set; }
    /// <summary>Total asset value on <see cref="ValuedOn"/>, expressed in <see cref="Currency"/>.</summary>
    public decimal Value { get; set; }
    /// <summary>ISO 4217 currency of <see cref="Value"/>.</summary>
    public string Currency { get; set; } = "EUR";
    /// <summary>User-facing origin of the estimate or market observation.</summary>
    public string Source { get; set; } = "Manual estimate";
    /// <summary>UTC timestamp at which LifeLedger stored this point.</summary>
    public DateTimeOffset RecordedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Represents a debt or other financial obligation.</summary>
public sealed class Liability
{
    /// <summary>Stable identifier of the liability.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the owning scenario.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the owning scenario.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>Name shown to the user.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Business category of the liability.</summary>
    public LiabilityKind Kind { get; set; }
    /// <summary>Principal currently outstanding in <see cref="Currency"/>.</summary>
    public decimal OutstandingBalance { get; set; }
    /// <summary>Share of the complete liability for which the profile is personally responsible, between zero and one.</summary>
    public decimal ResponsibilityRate { get; set; } = 1m;
    /// <summary>Annual interest rate expressed as a fraction.</summary>
    public decimal InterestRate { get; set; }
    /// <summary>Contractual payment made each month.</summary>
    public decimal MonthlyPayment { get; set; }
    /// <summary>Expected final payment date; null means it is not known.</summary>
    public DateOnly? PaidOffOn { get; set; }
    /// <summary>ISO 4217 currency of the balance and payment.</summary>
    public string Currency { get; set; } = "EUR";
    /// <summary>Asset allocations that explain what this liability finances.</summary>
    public List<AssetLiabilityLink> AssetLinks { get; set; } = [];
}

/// <summary>Represents a recurring or exceptional expense.</summary>
public sealed class Expense
{
    /// <summary>Stable identifier of the expense.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the owning scenario.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the owning scenario.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>Name shown to the user.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Whether this expense repeats or is one-off.</summary>
    public ExpenseKind Kind { get; set; }
    /// <summary>Cadence used when the expense is recurring.</summary>
    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;
    /// <summary>Amount per recurrence, expressed in <see cref="Currency"/>.</summary>
    public decimal MonthlyAmount { get; set; }
    /// <summary>Whether the amount rises with the scenario inflation rate.</summary>
    public bool IndexedToInflation { get; set; } = true;
    /// <summary>Whether a one-off expense is funded gradually in a dedicated monthly envelope before it is due.</summary>
    public bool SaveInAdvance { get; set; }
    /// <summary>First month in which LifeLedger should reserve money for a one-off expense; null starts with the scenario.</summary>
    public DateOnly? SavingsStartsOn { get; set; }
    /// <summary>First date on which the expense is active.</summary>
    public DateOnly StartsOn { get; set; }
    /// <summary>Last active date; null means it continues indefinitely.</summary>
    public DateOnly? EndsOn { get; set; }
    /// <summary>ISO 4217 currency of the expense amount.</summary>
    public string Currency { get; set; } = "EUR";
    /// <summary>Optional asset that causes this expense, such as monthly apartment charges.</summary>
    public Guid? LinkedAssetId { get; set; }
    /// <summary>Navigation to the asset that causes this expense.</summary>
    public Asset? LinkedAsset { get; set; }
    /// <summary>Optional bank category whose observed monthly average created or updates this planning assumption.</summary>
    public string? ObservedBankCategory { get; set; }
    /// <summary>Dated amount changes that replace the recurring amount from their effective month.</summary>
    public List<ExpenseAmountChange> AmountChanges { get; set; } = [];
}

/// <summary>Replaces a recurring expense amount from a chosen date without creating another expense.</summary>
public sealed class ExpenseAmountChange
{
    /// <summary>Stable identifier of the scheduled amount change.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the recurring expense that owns this change.</summary>
    public Guid ExpenseId { get; set; }
    /// <summary>Navigation to the owning recurring expense.</summary>
    public Expense? Expense { get; set; }
    /// <summary>First date from which <see cref="Amount"/> replaces the previous amount.</summary>
    public DateOnly EffectiveOn { get; set; }
    /// <summary>New amount per recurrence, expressed in the owning expense currency.</summary>
    public decimal Amount { get; set; }
}

/// <summary>Represents a regular contribution to an investment plan.</summary>
public sealed class InvestmentPlan
{
    /// <summary>Stable identifier of the plan.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the owning scenario.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the owning scenario.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>Name shown to the user.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Amount contributed each month.</summary>
    public decimal MonthlyContribution { get; set; }
    /// <summary>Expected annual gross return as a fraction.</summary>
    public decimal ExpectedAnnualReturn { get; set; }
    /// <summary>First date on which contributions are made.</summary>
    public DateOnly StartsOn { get; set; }
    /// <summary>Last contribution date; null means contributions continue.</summary>
    public DateOnly? EndsOn { get; set; }
}

/// <summary>Represents one explicit future sale of an existing asset and the destination of its net proceeds.</summary>
public sealed class PlannedAssetSale
{
    /// <summary>Stable identifier of the planned sale.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the owning scenario.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the owning scenario.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>Short name shown to the user.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Identifier of the asset that disappears from the balance sheet when sold.</summary>
    public Guid AssetId { get; set; }
    /// <summary>Navigation to the asset being sold.</summary>
    public Asset? Asset { get; set; }
    /// <summary>Calendar date on which the sale is applied to the monthly projection.</summary>
    public DateOnly HappensOn { get; set; }
    /// <summary>Whether the projected value on the sale date replaces the manually entered gross price.</summary>
    public bool UseProjectedValue { get; set; } = true;
    /// <summary>Gross manually estimated sale price in <see cref="Currency"/>; ignored when <see cref="UseProjectedValue"/> is true.</summary>
    public decimal GrossSalePrice { get; set; }
    /// <summary>Fixed selling costs such as agency, notary or brokerage fees in <see cref="Currency"/>.</summary>
    public decimal SellingCosts { get; set; }
    /// <summary>Estimated effective tax rate applied only to a positive capital gain.</summary>
    public decimal CapitalGainsTaxRate { get; set; }
    /// <summary>Optional country code explaining the sale-tax assumption.</summary>
    public string? CapitalGainsTaxCountryCode { get; set; }
    /// <summary>Whether outstanding liabilities allocated to the asset are repaid from the sale proceeds.</summary>
    public bool RepayLinkedLiabilities { get; set; } = true;
    /// <summary>Destination of the proceeds remaining after costs, tax and linked-debt repayment.</summary>
    public AssetSaleDestination Destination { get; set; } = AssetSaleDestination.Cash;
    /// <summary>Optional existing asset receiving the net proceeds when <see cref="Destination"/> is <see cref="AssetSaleDestination.Asset"/>.</summary>
    public Guid? DestinationAssetId { get; set; }
    /// <summary>Navigation to the optional destination asset.</summary>
    public Asset? DestinationAsset { get; set; }
    /// <summary>Optional investment plan receiving the net proceeds when <see cref="Destination"/> is <see cref="AssetSaleDestination.InvestmentPlan"/>.</summary>
    public Guid? DestinationInvestmentPlanId { get; set; }
    /// <summary>Navigation to the optional destination investment plan.</summary>
    public InvestmentPlan? DestinationInvestmentPlan { get; set; }
    /// <summary>ISO 4217 currency of manually entered price and cost amounts.</summary>
    public string Currency { get; set; } = "EUR";
    /// <summary>Optional user explanation for the planned transaction.</summary>
    public string Notes { get; set; } = string.Empty;
}

/// <summary>Represents a future life event that affects cash flow or net worth.</summary>
public sealed class ScenarioEvent
{
    /// <summary>Stable identifier of the event.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Identifier of the owning scenario.</summary>
    public Guid ScenarioId { get; set; }
    /// <summary>Navigation to the owning scenario.</summary>
    public FinancialScenario? Scenario { get; set; }
    /// <summary>Name shown to the user.</summary>
    public string Name { get; set; } = string.Empty;
    /// <summary>Business category of the event.</summary>
    public EventKind Kind { get; set; }
    /// <summary>Date on which the event first happens.</summary>
    public DateOnly HappensOn { get; set; }
    /// <summary>Optional cadence when the event repeats.</summary>
    public RecurrenceFrequency? RecurrenceFrequency { get; set; }
    /// <summary>Last repetition date; null means no explicit end.</summary>
    public DateOnly? RecurrenceEndsOn { get; set; }
    /// <summary>Cash impact applied once per occurrence.</summary>
    public decimal OneOffCashImpact { get; set; }
    /// <summary>Monthly cash impact while the event remains active.</summary>
    public decimal MonthlyCashImpact { get; set; }
    /// <summary>ISO 4217 currency of the one-off and monthly cash impacts.</summary>
    public string Currency { get; set; } = "EUR";
    /// <summary>Duration of the monthly impact; zero means no scheduled end.</summary>
    public int DurationMonths { get; set; }
    /// <summary>Free-form explanatory notes supplied by the user.</summary>
    public string Notes { get; set; } = string.Empty;
}
