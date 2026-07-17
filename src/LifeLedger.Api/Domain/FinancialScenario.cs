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
    /// <summary>Life events that change cash flow or net worth.</summary>
    public List<ScenarioEvent> Events { get; set; } = [];
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
    /// <summary>Expected annual gross return as a fraction.</summary>
    public decimal ExpectedAnnualReturn { get; set; }
    /// <summary>Expected annual price volatility as a fraction.</summary>
    public decimal Volatility { get; set; }
    /// <summary>Whether the asset can reasonably fund short-term spending.</summary>
    public bool IsLiquid { get; set; } = true;
    /// <summary>Optional public market ticker for an ETF or stock.</summary>
    public string? Ticker { get; set; }
    /// <summary>Number of market units held when a ticker is provided.</summary>
    public decimal Quantity { get; set; }
    /// <summary>Estimated effective tax rate applied to annual capital gains.</summary>
    public decimal CapitalGainsTaxRate { get; set; }
    /// <summary>Optional country code that explains the capital-gains assumption.</summary>
    public string? CapitalGainsTaxCountryCode { get; set; }
    /// <summary>ISO 4217 currency of the current value.</summary>
    public string Currency { get; set; } = "EUR";
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
    /// <summary>Annual interest rate expressed as a fraction.</summary>
    public decimal InterestRate { get; set; }
    /// <summary>Contractual payment made each month.</summary>
    public decimal MonthlyPayment { get; set; }
    /// <summary>Expected final payment date; null means it is not known.</summary>
    public DateOnly? PaidOffOn { get; set; }
    /// <summary>ISO 4217 currency of the balance and payment.</summary>
    public string Currency { get; set; } = "EUR";
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
    /// <summary>Duration of the monthly impact; zero means no scheduled end.</summary>
    public int DurationMonths { get; set; }
    /// <summary>Free-form explanatory notes supplied by the user.</summary>
    public string Notes { get; set; } = string.Empty;
}
