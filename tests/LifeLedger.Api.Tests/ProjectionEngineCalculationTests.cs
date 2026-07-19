using LifeLedger.Api.Contracts;
using LifeLedger.Api.Domain;
using LifeLedger.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies the complete projection arithmetic with small, independently calculable scenarios.</summary>
public sealed class ProjectionEngineCalculationTests : IClassFixture<LifeLedgerApiFactory>
{
    private readonly LifeLedgerApiFactory _factory;

    /// <summary>Creates calculation tests with the application's real projection dependencies.</summary>
    public ProjectionEngineCalculationTests(LifeLedgerApiFactory factory) => _factory = factory;

    /// <summary>Compounds an ETF return into final wealth without presenting unrealised appreciation as cash income.</summary>
    [Fact]
    public void Etf_return_increases_final_wealth_but_not_cash_passive_income()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(assets: [Asset("ETF", 10_000m, 0.04m)]);

        var simulation = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));
        var dashboard = engine.BuildDashboard(scenario);

        Assert.Equal(10_400m, simulation.Timeline[1].NetWorth);
        Assert.Equal(0m, simulation.Timeline[1].PassiveIncome);
        Assert.Equal(0m, dashboard.PassiveMonthlyIncome);
        Assert.Equal(33.33m, Math.Round(dashboard.ExpectedMonthlyPortfolioGrowth, 2));
    }

    /// <summary>Uses value-weighted returns so a zero-return cash balance does not erase an ETF's gain.</summary>
    [Fact]
    public void Mixed_asset_returns_are_weighted_by_current_value()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(assets: [Asset("ETF", 10_000m, 0.04m), Asset("Cash", 10_000m, 0m, AssetKind.Cash)]);

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));

        Assert.Equal(20_400m, result.Timeline[1].NetWorth);
    }

    /// <summary>Projects each asset category independently and reconciles every component to total net worth.</summary>
    [Fact]
    public void Wealth_timeline_exposes_category_growth_cash_and_debt_components()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(assets:
        [
            Asset("Home", 100_000m, 0.03m, AssetKind.RealEstate),
            Asset("World ETF", 50_000m, 0.06m),
            Asset("Bank account", 10_000m, 0m, AssetKind.Cash)
        ], incomes: [new IncomeStream { Name = "Income", Kind = IncomeKind.Freelance, AmountMode = IncomeAmountMode.Monthly, MonthlyAmount = 100m, StartsOn = Start, Currency = "EUR" }]);
        scenario.Liabilities = [new Liability { Name = "Mortgage", Kind = LiabilityKind.Mortgage, OutstandingBalance = 20_000m, MonthlyPayment = 0m, Currency = "EUR" }];

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));
        var final = result.Timeline[1];

        Assert.Contains(final.WealthComponents, component => component.Category == nameof(AssetKind.RealEstate) && component.Value == 103_000m);
        Assert.Contains(final.WealthComponents, component => component.Category == nameof(AssetKind.Etf) && component.Value == 53_000m);
        Assert.Contains(final.WealthComponents, component => component.Type == ProjectionWealthComponentType.ProjectedCash && component.Value == 1_200m);
        Assert.Contains(final.WealthComponents, component => component.Type == ProjectionWealthComponentType.Liability && component.Value == -20_000m);
        Assert.Equal(final.NetWorth, final.WealthComponents.Sum(component => component.Value));
    }

    /// <summary>Uses available cash before a non-liquid property when spending must be funded from existing wealth.</summary>
    [Fact]
    public void Cash_shortfall_liquidates_cash_before_property_and_preserves_the_component_sum()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var home = Asset("Home", 1_000m, 0m, AssetKind.RealEstate);
        home.IsLiquid = false;
        var scenario = Scenario(
            assets: [Asset("Cash", 500m, 0m, AssetKind.Cash), home],
            expenses: [new Expense { Name = "Living costs", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Monthly, MonthlyAmount = 50m, StartsOn = Start, Currency = "EUR" }]);

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));
        var final = result.Timeline[1];

        Assert.Contains(final.WealthComponents, component => component.Category == nameof(AssetKind.Cash) && component.Value == 0m);
        Assert.Contains(final.WealthComponents, component => component.Category == nameof(AssetKind.RealEstate) && component.Value == 900m);
        Assert.Contains(final.WealthComponents, component => component.Type == ProjectionWealthComponentType.ProjectedCash && component.Value == 0m);
        Assert.Equal(900m, final.NetWorth);
        Assert.Equal(final.NetWorth, final.WealthComponents.Sum(component => component.Value));
    }

    /// <summary>Sells a property explicitly, deducts fees and gain tax, settles its linked mortgage and exposes the full breakdown.</summary>
    [Fact]
    public void Planned_property_sale_transfers_net_proceeds_to_cash_after_costs_tax_and_debt()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var home = Asset("Home", 100_000m, 0m, AssetKind.RealEstate);
        home.PurchasePrice = 30_000m;
        var mortgage = new Liability { Id = Guid.NewGuid(), Name = "Mortgage", Kind = LiabilityKind.Mortgage, OutstandingBalance = 20_000m, MonthlyPayment = 0m, Currency = "EUR" };
        home.LiabilityLinks = [new AssetLiabilityLink { AssetId = home.Id, LiabilityId = mortgage.Id, AllocationRate = 1m }];
        var scenario = Scenario(assets: [home]);
        scenario.Liabilities = [mortgage];
        scenario.AssetSales =
        [
            new PlannedAssetSale
            {
                Name = "Sell the home", AssetId = home.Id, HappensOn = new DateOnly(2026, 6, 15), UseProjectedValue = true,
                SellingCosts = 10_000m, CapitalGainsTaxRate = 0.20m, RepayLinkedLiabilities = true,
                Destination = AssetSaleDestination.Cash, Currency = "EUR"
            }
        ];

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));
        var final = result.Timeline[1];
        var sale = Assert.Single(final.AssetSales);

        Assert.Equal(100_000m, sale.GrossProceeds);
        Assert.Equal(10_000m, sale.SellingCosts);
        Assert.Equal(14_000m, sale.CapitalGainsTax);
        Assert.Equal(20_000m, sale.DebtRepaid);
        Assert.Equal(56_000m, sale.NetProceeds);
        Assert.Equal(56_000m, final.NetWorth);
        Assert.Contains(final.WealthComponents, component => component.Type == ProjectionWealthComponentType.ProjectedCash && component.Value == 56_000m);
        Assert.Contains(final.WealthComponents, component => component.Type == ProjectionWealthComponentType.Liability && component.Value == 0m);
    }

    /// <summary>Moves manual sale proceeds into another owned asset without counting the transfer as passive income.</summary>
    [Fact]
    public void Planned_sale_can_reinvest_net_proceeds_in_an_existing_asset()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var home = Asset("Home", 100_000m, 0m, AssetKind.RealEstate);
        var etf = Asset("ETF", 50_000m, 0m);
        var scenario = Scenario(assets: [home, etf]);
        scenario.AssetSales =
        [
            new PlannedAssetSale
            {
                Name = "Sell and reinvest", AssetId = home.Id, HappensOn = new DateOnly(2026, 6, 1), UseProjectedValue = false,
                GrossSalePrice = 120_000m, Destination = AssetSaleDestination.Asset, DestinationAssetId = etf.Id, Currency = "EUR"
            }
        ];

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));
        var final = result.Timeline[1];

        Assert.Equal(170_000m, final.NetWorth);
        Assert.Equal(0m, final.PassiveIncome);
        Assert.Contains(final.WealthComponents, component => component.Category == nameof(AssetKind.RealEstate) && component.Value == 0m);
        Assert.Contains(final.WealthComponents, component => component.Category == nameof(AssetKind.Etf) && component.Value == 170_000m);
    }

    /// <summary>Applies salary growth and declared tax exactly once to the annual income and final wealth.</summary>
    [Fact]
    public void Salary_tax_and_growth_flow_into_the_annual_totals()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(
            assets: [Asset("Cash", 1_000m, 0m, AssetKind.Cash)],
            incomes:
            [
                new IncomeStream
                {
                    Name = "Salary", Kind = IncomeKind.Salary, AmountMode = IncomeAmountMode.Monthly, MonthlyAmount = 1_000m,
                    AnnualGrowthRate = 0.05m, IsTaxable = true, TaxRate = 0.20m, StartsOn = Start, Currency = "EUR"
                }
            ]);

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 2));

        Assert.Equal(9_600m, result.Timeline[1].Income);
        Assert.Equal(10_080m, result.Timeline[2].Income);
        Assert.Equal(20_680m, result.Timeline[2].NetWorth);
    }

    /// <summary>Reports after-tax rent as passive cash income and includes the same cash in final wealth.</summary>
    [Fact]
    public void Rental_income_is_taxed_reported_and_added_to_wealth()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(
            assets: [Asset("Cash", 1_000m, 0m, AssetKind.Cash)],
            incomes:
            [
                new IncomeStream
                {
                    Name = "Rent", Kind = IncomeKind.Rental, AmountMode = IncomeAmountMode.Monthly, MonthlyAmount = 1_000m,
                    IsTaxable = true, TaxRate = 0.20m, StartsOn = Start, Currency = "EUR"
                }
            ]);

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));
        var dashboard = engine.BuildDashboard(scenario);

        Assert.Equal(9_600m, result.Timeline[1].PassiveIncome);
        Assert.Equal(800m, dashboard.PassiveMonthlyIncome);
        Assert.Equal(10_600m, result.Timeline[1].NetWorth);
    }

    /// <summary>Lets an unfunded recurring cost make projected wealth negative instead of silently stopping at zero.</summary>
    [Fact]
    public void Costs_beyond_available_assets_make_the_plan_insolvent()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(expenses: [new Expense { Name = "Living costs", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Monthly, MonthlyAmount = 100m, StartsOn = Start, Currency = "EUR" }]);

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));

        Assert.Equal(-1_200m, result.Timeline[1].NetWorth);
        Assert.Equal(0m, result.ProbabilityOfSuccess);
        Assert.Contains(result.Warnings, warning => warning.Code == "insolvency-age" && warning.Value == 37m);
        Assert.Contains(result.Warnings, warning => warning.Code == "low-emergency-fund" && warning.Value is null);
    }

    /// <summary>Does not invent investment growth for income accumulated in a scenario that owns no yielding asset.</summary>
    [Fact]
    public void Income_without_an_asset_does_not_receive_an_invented_return()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(incomes: [new IncomeStream { Name = "Freelance", Kind = IncomeKind.Freelance, AmountMode = IncomeAmountMode.Monthly, MonthlyAmount = 100m, StartsOn = Start, Currency = "EUR" }]);

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));

        Assert.Equal(1_200m, result.Timeline[1].NetWorth);
    }

    /// <summary>Keeps cash stable in Monte Carlo when both its declared return and volatility are zero.</summary>
    [Fact]
    public void Zero_volatility_cash_is_not_randomised_by_monte_carlo()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(assets: [Asset("Cash", 10_000m, 0m, AssetKind.Cash)]);
        scenario.Assumptions.MonteCarloRuns = 50;
        scenario.Assumptions.DefaultReturnVolatility = 0.12m;

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.MonteCarlo, Years: 1, Runs: 50));

        Assert.All(result.TerminalNetWorths, value => Assert.Equal(10_000m, value));
    }

    /// <summary>Starts the illustrative historical cycle with its first documented annual return.</summary>
    [Fact]
    public void Historical_mode_starts_with_the_first_cycle_year()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(assets: [Asset("Portfolio", 10_000m, 0m)]);

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Historical, Years: 1));

        Assert.Equal(11_800m, result.Timeline[1].NetWorth);
    }

    /// <summary>Compounds an inflation-indexed monthly cost from its own start date and subtracts every occurrence.</summary>
    [Fact]
    public void Inflation_indexed_costs_increase_month_by_month()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(
            assets: [Asset("Cash", 20_000m, 0m, AssetKind.Cash)],
            expenses: [new Expense { Name = "Living costs", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Monthly, MonthlyAmount = 100m, IndexedToInflation = true, StartsOn = Start, Currency = "EUR" }]);
        scenario.Assumptions.InflationRate = 0.12m;
        var expectedExpenses = Enumerable.Range(0, 12).Sum(month => 100m * (decimal)Math.Pow(1.12d, month / 12d));

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));

        Assert.Equal(Math.Round(expectedExpenses, 2), result.Timeline[1].Expenses);
        Assert.Equal(Math.Round(20_000m - expectedExpenses, 2), result.Timeline[1].NetWorth);
    }

    /// <summary>Applies the declared capital-gains tax rate to the asset return exactly once.</summary>
    [Fact]
    public void Capital_gains_tax_reduces_the_compounded_asset_return()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var etf = Asset("ETF", 10_000m, 0.04m);
        etf.CapitalGainsTaxRate = 0.25m;
        var scenario = Scenario(assets: [etf]);

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));

        Assert.Equal(10_300m, result.Timeline[1].NetWorth);
    }

    /// <summary>Reduces cash and principal together so a zero-interest debt payment does not change net worth.</summary>
    [Fact]
    public void Debt_principal_payments_reduce_cash_and_debt_without_double_counting()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(assets: [Asset("Cash", 1_200m, 0m, AssetKind.Cash)]);
        scenario.Liabilities =
        [
            new Liability { Name = "Loan", Kind = LiabilityKind.Loan, OutstandingBalance = 1_200m, MonthlyPayment = 100m, InterestRate = 0m, Currency = "EUR" }
        ];

        var result = engine.Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic, Years: 1));

        Assert.Equal(0m, result.Timeline[0].NetWorth);
        Assert.Equal(1_200m, result.Timeline[1].Expenses);
        Assert.Equal(0m, result.Timeline[1].NetWorth);
    }

    /// <summary>Uses a complete projected expense year and returns a real calendar date for financial independence.</summary>
    [Fact]
    public void Financial_independence_does_not_use_the_zero_expense_snapshot_or_add_a_calendar_year_as_an_offset()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(
            assets: [Asset("Portfolio", 500_000m, 0m)],
            expenses: [new Expense { Name = "Living costs", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Monthly, MonthlyAmount = 1_000m, StartsOn = Start, Currency = "EUR" }]);
        scenario.Assumptions.SafeWithdrawalRate = 0.04m;

        var dashboard = engine.BuildDashboard(scenario);

        Assert.Equal(new DateOnly(2027, 1, 1), dashboard.FinancialIndependenceDate);
    }

    /// <summary>Returns language-neutral warning codes and numeric values for every parameterised risk signal.</summary>
    [Fact]
    public void Risk_warnings_are_structured_for_client_side_translation()
    {
        using var scope = _factory.Services.CreateScope();
        var engine = scope.ServiceProvider.GetRequiredService<IProjectionEngine>();
        var scenario = Scenario(
            assets: [Asset("Cash", 1_000m, 0m, AssetKind.Cash)],
            incomes: [new IncomeStream { Name = "Income", Kind = IncomeKind.Freelance, AmountMode = IncomeAmountMode.Monthly, MonthlyAmount = 100m, StartsOn = Start, Currency = "EUR" }],
            expenses: [new Expense { Name = "Living costs", Kind = ExpenseKind.Recurring, Frequency = RecurrenceFrequency.Monthly, MonthlyAmount = 105m, StartsOn = Start, Currency = "EUR" }]);
        scenario.Assumptions.InflationRate = 0.10m;
        scenario.Liabilities = [new Liability { Name = "Debt", Kind = LiabilityKind.Loan, OutstandingBalance = 100m, MonthlyPayment = 50m, Currency = "EUR" }];

        var dashboard = engine.BuildDashboard(scenario);

        Assert.Contains(dashboard.Warnings, warning => warning.Code == "purchasing-power-drop" && warning.Value > 0m);
        Assert.Contains(dashboard.Warnings, warning => warning.Code == "high-debt-payments" && warning.Value is null);
        Assert.Contains(dashboard.Warnings, warning => warning.Code == "low-monte-carlo-success" && warning.Value is >= 0m and < 80m);
    }

    private static readonly DateOnly Start = new(2026, 1, 1);

    /// <summary>Creates a minimal EUR scenario that isolates one projection rule at a time.</summary>
    private static FinancialScenario Scenario(IReadOnlyList<Asset>? assets = null, IReadOnlyList<IncomeStream>? incomes = null, IReadOnlyList<Expense>? expenses = null) => new()
    {
        Id = Guid.NewGuid(),
        Name = "Calculation test",
        StartsOn = Start,
        Profile = new Profile { DisplayName = "Calculation profile", BaseCurrency = "EUR", BirthDate = new DateOnly(1990, 1, 1), ExpectedLifespan = 90 },
        Assumptions = new SimulationAssumptions { InflationRate = 0m, RetirementAge = 90, MonteCarloRuns = 50, DefaultReturnVolatility = 0.12m },
        Assets = assets?.ToList() ?? [],
        Incomes = incomes?.ToList() ?? [],
        Expenses = expenses?.ToList() ?? []
    };

    /// <summary>Creates one asset with explicit return assumptions and no capital-gains tax.</summary>
    private static Asset Asset(string name, decimal value, decimal annualReturn, AssetKind kind = AssetKind.Etf) => new()
    {
        Name = name, Kind = kind, CurrentValue = value, ExpectedAnnualReturn = annualReturn, Volatility = 0m, CapitalGainsTaxRate = 0m, Currency = "EUR"
    };
}
