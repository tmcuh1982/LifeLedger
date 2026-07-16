namespace LifeLedger.Api.Domain;

public sealed class FinancialScenario
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ProfileId { get; set; }
    public Profile? Profile { get; set; }
    public Guid? ParentScenarioId { get; set; }
    public string Name { get; set; } = "Base plan";
    public string Description { get; set; } = string.Empty;
    public bool IsBaseline { get; set; }
    public DateOnly StartsOn { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public SimulationAssumptions Assumptions { get; set; } = new();
    public List<IncomeStream> Incomes { get; set; } = [];
    public List<Asset> Assets { get; set; } = [];
    public List<Liability> Liabilities { get; set; } = [];
    public List<Expense> Expenses { get; set; } = [];
    public List<InvestmentPlan> Investments { get; set; } = [];
    public List<ScenarioEvent> Events { get; set; } = [];
}

public sealed class SimulationAssumptions
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public FinancialScenario? Scenario { get; set; }
    public decimal InflationRate { get; set; } = 0.025m;
    public decimal SalaryGrowthRate { get; set; } = 0.02m;
    public decimal SafeWithdrawalRate { get; set; } = 0.04m;
    public int RetirementAge { get; set; } = 65;
    public int MonteCarloRuns { get; set; } = 500;
    public decimal DefaultReturnVolatility { get; set; } = 0.12m;
}

public sealed class IncomeStream
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public FinancialScenario? Scenario { get; set; }
    public string Name { get; set; } = string.Empty;
    public IncomeKind Kind { get; set; }
    public decimal MonthlyAmount { get; set; }
    public decimal AnnualGrowthRate { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public bool IsTaxable { get; set; } = true;
    public decimal TaxRate { get; set; }
    public string? TaxCountryCode { get; set; }
    public string Currency { get; set; } = "EUR";
}

public sealed class Asset
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public FinancialScenario? Scenario { get; set; }
    public string Name { get; set; } = string.Empty;
    public AssetKind Kind { get; set; }
    public decimal CurrentValue { get; set; }
    public decimal ExpectedAnnualReturn { get; set; }
    public decimal Volatility { get; set; }
    public bool IsLiquid { get; set; } = true;
    public string? Ticker { get; set; }
    public decimal Quantity { get; set; }
    public decimal CapitalGainsTaxRate { get; set; }
    public string? CapitalGainsTaxCountryCode { get; set; }
    public string Currency { get; set; } = "EUR";
}

public sealed class AssetQuoteSnapshot
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssetId { get; set; }
    public Asset? Asset { get; set; }
    public DateTimeOffset CapturedAt { get; set; } = DateTimeOffset.UtcNow;
    public decimal Price { get; set; }
    public string Currency { get; set; } = "EUR";
    public string Source { get; set; } = "Yahoo Finance";
}

public sealed class Liability
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public FinancialScenario? Scenario { get; set; }
    public string Name { get; set; } = string.Empty;
    public LiabilityKind Kind { get; set; }
    public decimal OutstandingBalance { get; set; }
    public decimal InterestRate { get; set; }
    public decimal MonthlyPayment { get; set; }
    public DateOnly? PaidOffOn { get; set; }
    public string Currency { get; set; } = "EUR";
}

public sealed class Expense
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public FinancialScenario? Scenario { get; set; }
    public string Name { get; set; } = string.Empty;
    public ExpenseKind Kind { get; set; }
    public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;
    public decimal MonthlyAmount { get; set; }
    public bool IndexedToInflation { get; set; } = true;
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
    public string Currency { get; set; } = "EUR";
}

public sealed class InvestmentPlan
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public FinancialScenario? Scenario { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal MonthlyContribution { get; set; }
    public decimal ExpectedAnnualReturn { get; set; }
    public DateOnly StartsOn { get; set; }
    public DateOnly? EndsOn { get; set; }
}

public sealed class ScenarioEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ScenarioId { get; set; }
    public FinancialScenario? Scenario { get; set; }
    public string Name { get; set; } = string.Empty;
    public EventKind Kind { get; set; }
    public DateOnly HappensOn { get; set; }
    public RecurrenceFrequency? RecurrenceFrequency { get; set; }
    public DateOnly? RecurrenceEndsOn { get; set; }
    public decimal OneOffCashImpact { get; set; }
    public decimal MonthlyCashImpact { get; set; }
    public int DurationMonths { get; set; }
    public string Notes { get; set; } = string.Empty;
}
