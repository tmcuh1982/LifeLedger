using LifeLedger.Api.Contracts;
using LifeLedger.Api.Domain;

namespace LifeLedger.Api.Services;

/// <summary>Provides the current annual state to a projection plugin.</summary>
public sealed record ProjectionContext(FinancialScenario Scenario, int YearIndex, DateOnly Date, int Age, decimal InflationRate);

/// <summary>Extension point for country, tax and planning plugins to alter an annual projection.</summary>
public interface IProjectionModifier
{
    /// <summary>Adjusts one projected year without mutating the source scenario.</summary>
    void Apply(ProjectionContext context, ProjectionAdjustment adjustment);
}

/// <summary>Collects additive changes requested by one or more projection plugins.</summary>
public sealed class ProjectionAdjustment
{
    /// <summary>Amount added to annual income.</summary>
    public decimal IncomeDelta { get; set; }
    /// <summary>Amount added to annual expenses.</summary>
    public decimal ExpenseDelta { get; set; }
    /// <summary>Amount added directly to the asset value.</summary>
    public decimal NetWorthDelta { get; set; }
    /// <summary>Human-readable explanations contributed by plugins.</summary>
    public List<string> Notes { get; } = [];
}

/// <summary>Builds dashboard summaries and lifetime financial simulations.</summary>
public interface IProjectionEngine
{
    /// <summary>Projects a scenario according to the requested simulation mode.</summary>
    SimulationResponse Simulate(FinancialScenario scenario, SimulationRequest request);
    /// <summary>Builds the dashboard metrics and charts for a scenario.</summary>
    DashboardResponse BuildDashboard(FinancialScenario scenario);
}

/// <summary>Local deterministic, historical, and Monte Carlo projection engine.</summary>
public sealed class ProjectionEngine(ICurrencyService currencies, IEnumerable<IProjectionModifier> modifiers) : IProjectionEngine
{
    /// <summary>Converts each source amount to the profile's base currency.</summary>
    private readonly ICurrencyService _currencies = currencies;
    /// <summary>Plugins captured once at startup to keep each simulation deterministic in composition.</summary>
    private readonly IProjectionModifier[] _modifiers = modifiers.ToArray();
    /// <summary>Illustrative annual return sequence used by historical simulation mode.</summary>
    private static readonly decimal[] HistoricalReturns = [0.18m, 0.12m, -0.21m, 0.08m, 0.16m, -0.11m, 0.23m, 0.06m, -0.07m, 0.14m, 0.09m, -0.18m];
    /// <summary>Illustrative annual inflation sequence paired with historical simulation mode.</summary>
    private static readonly decimal[] HistoricalInflation = [0.018m, 0.021m, 0.026m, 0.014m, 0.031m, 0.047m, 0.022m, 0.016m, 0.038m, 0.027m, 0.019m, 0.024m];

    /// <inheritdoc />
    public DashboardResponse BuildDashboard(FinancialScenario scenario)
    {
        var deterministic = Simulate(scenario, new SimulationRequest(SimulationMode.Deterministic));
        var monteCarlo = Simulate(scenario, new SimulationRequest(SimulationMode.MonteCarlo, Runs: scenario.Assumptions.MonteCarloRuns));
        var timeline = deterministic.Timeline;
        var first = timeline.First();
        var last = timeline.Last();
        var retirementIncome = GetRetirementIncome(scenario);
        var passiveIncome = scenario.Incomes
            .Where(x => x.Kind is IncomeKind.Rental or IncomeKind.Dividends or IncomeKind.Royalties)
            .Sum(x => ToBase(scenario, x.MonthlyAmount, x.Currency));
        // Financial independence is reached when the planned withdrawal rate covers annual expenses.
        var fi = timeline.FirstOrDefault(x => x.NetWorth * scenario.Assumptions.SafeWithdrawalRate >= x.Expenses)?.Year;
        DateOnly? fiDate = fi is null ? null : scenario.StartsOn.AddYears(fi.Value);
        var purchasingPowerChange = first.NetWorth == 0 ? 0 : (last.InflationAdjustedNetWorth / first.NetWorth - 1m) * 100m;
        var allocation = BuildAllocation(scenario);
        var warnings = deterministic.Warnings.Concat(monteCarlo.Warnings).Distinct().Take(5).ToArray();

        return new DashboardResponse(
            scenario.Id,
            scenario.Name,
            scenario.Profile!.BaseCurrency,
            first.NetWorth,
            last.NetWorth,
            passiveIncome,
            retirementIncome,
            fiDate,
            purchasingPowerChange,
            monteCarlo.ProbabilityOfSuccess,
            timeline,
            allocation,
            warnings);
    }

    /// <inheritdoc />
    public SimulationResponse Simulate(FinancialScenario scenario, SimulationRequest request)
    {
        var years = Math.Clamp(request.Years ?? Math.Max(1, scenario.Profile!.ExpectedLifespan - AgeAt(scenario.Profile.BirthDate, scenario.StartsOn)), 1, 80);
        var runs = request.Mode == SimulationMode.MonteCarlo
            ? Math.Clamp(request.Runs ?? scenario.Assumptions.MonteCarloRuns, 50, 5_000)
            : 1;
        // Monte Carlo returns a stable reference timeline alongside the distribution of random paths.
        var deterministicTimeline = Project(scenario, years, request.Mode == SimulationMode.MonteCarlo ? SimulationMode.Deterministic : request.Mode, random: null);

        if (request.Mode != SimulationMode.MonteCarlo)
        {
            return new SimulationResponse(request.Mode, 1, deterministicTimeline.All(x => x.NetWorth >= 0) ? 1m : 0m, deterministicTimeline, [deterministicTimeline[^1].NetWorth], BuildWarnings(scenario, deterministicTimeline));
        }

        // Each seeded random path is reproducible, which makes unexpected simulation changes testable.
        var successfulRuns = 0;
        var terminalValues = new decimal[runs];
        for (var run = 0; run < runs; run++)
        {
            var projection = Project(scenario, years, SimulationMode.MonteCarlo, new Random(unchecked(17_113 + run * 7_919)));
            terminalValues[run] = projection[^1].NetWorth;
            if (projection.All(x => x.NetWorth >= 0)) successfulRuns++;
        }

        var probability = Math.Round((decimal)successfulRuns / runs, 4);
        var warnings = BuildWarnings(scenario, deterministicTimeline).ToList();
        if (probability < 0.8m) warnings.Add($"Only {probability:P0} of simulated paths stay solvent through the planned horizon.");
        return new SimulationResponse(SimulationMode.MonteCarlo, runs, probability, deterministicTimeline, terminalValues.Order().ToArray(), warnings.Distinct().ToArray());
    }

    /// <summary>Projects monthly cash flow internally and returns one clear summary row for each future year.</summary>
    private IReadOnlyList<ProjectionYear> Project(FinancialScenario scenario, int years, SimulationMode mode, Random? random)
    {
        var profile = scenario.Profile!;
        var assumptions = scenario.Assumptions;
        // All calculations are consolidated in the profile's base currency before aggregation.
        var assetValue = scenario.Assets.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency));
        var debt = scenario.Liabilities.Sum(x => ToBase(scenario, x.OutstandingBalance, x.Currency));
        var initialNetWorth = assetValue - debt;
        var weightedReturn = WeightedReturn(scenario.Assets, scenario);
        var weightedVolatility = WeightedVolatility(scenario.Assets, scenario, assumptions.DefaultReturnVolatility);
        var rows = new List<ProjectionYear>(years + 1);
        var inflationIndex = 1m;
        var sinkingFundBalances = new Dictionary<Guid, decimal>();

        // The initial row is an exact snapshot, before any future income or expense is applied.
        rows.Add(new ProjectionYear(
            scenario.StartsOn.Year,
            AgeAt(profile.BirthDate, scenario.StartsOn),
            Math.Round(initialNetWorth, 2),
            0m,
            0m,
            0m,
            0m,
            Math.Round(initialNetWorth, 2),
            0m,
            0m));

        for (var year = 1; year <= years; year++)
        {
            var annualInflation = mode == SimulationMode.Historical ? HistoricalInflation[year % HistoricalInflation.Length] : assumptions.InflationRate;
            var annualReturn = mode switch
            {
                SimulationMode.Historical => HistoricalReturns[year % HistoricalReturns.Length],
                SimulationMode.MonteCarlo => weightedReturn + Normal(random!) * weightedVolatility,
                _ => weightedReturn
            };
            var monthlyReturn = MonthlyRate(annualReturn);
            var monthlyInflation = MonthlyRate(annualInflation);
            var contextDate = scenario.StartsOn.AddYears(year);
            var contextAge = AgeAt(profile.BirthDate, contextDate);
            // Plugins remain annual extension points; their deltas are distributed across the twelve monthly steps.
            var adjustment = new ProjectionAdjustment();
            var context = new ProjectionContext(scenario, year, contextDate, contextAge, annualInflation);
            foreach (var modifier in _modifiers) modifier.Apply(context, adjustment);

            var cashFlow = 0m;
            var income = 0m;
            var expenses = 0m;
            var passiveIncome = 0m;
            var plannedSavings = 0m;
            for (var month = 0; month < 12; month++)
            {
                var date = scenario.StartsOn.AddMonths((year - 1) * 12 + month);
                var age = AgeAt(profile.BirthDate, date);
                var monthlyIncome = AnnualIncome(scenario, date, age, assumptions.RetirementAge, assumptions.InflationRate) / 12m;
                var monthlyPassiveIncome = AnnualPassiveIncome(scenario, date, age, assumptions.RetirementAge) / 12m;
                var monthlyPublicPension = age >= assumptions.RetirementAge ? GetRetirementIncome(scenario) : 0m;
                var monthlyExpenses = MonthlyExpenses(scenario, date, MonthsBetween(scenario.StartsOn, date), assumptions.InflationRate);
                var monthlyContributions = MonthlyContributions(scenario, date);
                var monthlyDebtPayment = MonthlyDebtPayments(scenario, date);
                var monthlyEventImpact = MonthlyEventImpact(scenario, date);
                var (monthlySavings, duePayment) = ProcessSinkingFunds(scenario, date, sinkingFundBalances);
                var monthlyCashFlow = monthlyIncome + monthlyPassiveIncome + monthlyPublicPension + adjustment.IncomeDelta / 12m
                    - monthlyExpenses - monthlyDebtPayment - monthlyContributions - monthlySavings - adjustment.ExpenseDelta / 12m + monthlyEventImpact;

                // Savings and investment contributions remain assets until spent, while the planned purchase is paid only in its due month.
                assetValue = Math.Max(0m, assetValue * (1m + monthlyReturn) + monthlyCashFlow + monthlyContributions + monthlySavings - duePayment + (month == 11 ? adjustment.NetWorthDelta : 0m));
                debt = AdvanceDebtMonthly(scenario, debt, date);
                inflationIndex *= 1m + monthlyInflation;

                cashFlow += monthlyCashFlow;
                income += monthlyIncome + monthlyPassiveIncome + monthlyPublicPension + adjustment.IncomeDelta / 12m;
                expenses += monthlyExpenses + monthlyDebtPayment + monthlyContributions + monthlySavings + adjustment.ExpenseDelta / 12m;
                passiveIncome += monthlyPassiveIncome;
                plannedSavings += monthlySavings;
            }

            var netWorth = assetValue - debt;
            rows.Add(new ProjectionYear(
                contextDate.Year,
                contextAge,
                Math.Round(netWorth, 2),
                Math.Round(cashFlow, 2),
                Math.Round(income, 2),
                Math.Round(expenses, 2),
                Math.Round(passiveIncome, 2),
                Math.Round(netWorth / inflationIndex, 2),
                Math.Round(plannedSavings, 2),
                Math.Round(sinkingFundBalances.Values.Sum(), 2)));
        }

        return rows;
    }

    /// <summary>Calculates annual non-passive income after growth, inflation indexing, and declared tax.</summary>
    private decimal AnnualIncome(FinancialScenario scenario, DateOnly date, int age, int retirementAge, decimal inflationRate) => scenario.Incomes
        .Where(x => x.Kind is not (IncomeKind.Rental or IncomeKind.Dividends or IncomeKind.Royalties or IncomeKind.Pension))
        .Where(x => IsActive(x.StartsOn, x.EndsOn, date) && (x.Kind != IncomeKind.Salary || age < retirementAge))
        .Sum(x =>
        {
            // Salary growth is expressed in real terms; headline inflation is added to preserve purchasing power.
            var growth = x.AnnualGrowthRate + (x.Kind == IncomeKind.Salary ? inflationRate : 0m);
            var grossIncome = x.MonthlyAmount * 12m * Pow(1m + growth, YearsBetween(x.StartsOn, date));
            return ToBase(scenario, AfterIncomeTax(x, grossIncome), x.Currency);
        });

    /// <summary>Calculates annual rental, dividend, and royalty income after declared tax.</summary>
    private decimal AnnualPassiveIncome(FinancialScenario scenario, DateOnly date, int age, int retirementAge) => scenario.Incomes
        .Where(x => x.Kind is IncomeKind.Rental or IncomeKind.Dividends or IncomeKind.Royalties)
        .Where(x => IsActive(x.StartsOn, x.EndsOn, date))
        .Sum(x => ToBase(scenario, AfterIncomeTax(x, x.MonthlyAmount * 12m * Pow(1m + x.AnnualGrowthRate, YearsBetween(x.StartsOn, date))), x.Currency));

    /// <summary>Applies an effective tax rate clamped to a valid fraction.</summary>
    private static decimal AfterIncomeTax(IncomeStream income, decimal grossIncome) => income.IsTaxable
        ? grossIncome * (1m - Math.Clamp(income.TaxRate, 0m, 1m))
        : grossIncome;

    /// <summary>Calculates ordinary expenses for one month; one-off expenses are charged only in their due month.</summary>
    private decimal MonthlyExpenses(FinancialScenario scenario, DateOnly date, int elapsedMonths, decimal inflationRate) => scenario.Expenses
        .Where(expense => expense.Kind == ExpenseKind.Recurring
            ? IsActive(expense.StartsOn, expense.EndsOn, date)
            : SameMonth(expense.StartsOn, date) && !expense.SaveInAdvance)
        .Sum(expense => ToBase(
            scenario,
            expense.MonthlyAmount * OccurrencesPerMonth(expense, date) * (expense.IndexedToInflation ? DecimalPow(1m + inflationRate, elapsedMonths / 12m) : 1m),
            expense.Currency));

    /// <summary>Converts an expense recurrence into the expected occurrences in one projection month.</summary>
    private static decimal OccurrencesPerMonth(Expense expense, DateOnly date)
    {
        if (expense.Kind == ExpenseKind.Exceptional) return 1m;

        return expense.Frequency switch
        {
            RecurrenceFrequency.Daily => DateTime.DaysInMonth(date.Year, date.Month),
            RecurrenceFrequency.Weekly => DateTime.DaysInMonth(date.Year, date.Month) / 7m,
            RecurrenceFrequency.EveryTwoWeeks => DateTime.DaysInMonth(date.Year, date.Month) / 14m,
            RecurrenceFrequency.Monthly => 1m,
            RecurrenceFrequency.Quarterly => MonthsBetween(expense.StartsOn, date) % 3 == 0 ? 1m : 0m,
            RecurrenceFrequency.Yearly => MonthsBetween(expense.StartsOn, date) % 12 == 0 ? 1m : 0m,
            RecurrenceFrequency.EveryFiveYears => MonthsBetween(expense.StartsOn, date) % 60 == 0 ? 1m : 0m,
            _ => 1m
        };
    }

    /// <summary>Calculates investment contributions active in the selected month.</summary>
    private static decimal MonthlyContributions(FinancialScenario scenario, DateOnly date) => scenario.Investments
        .Where(x => IsActive(x.StartsOn, x.EndsOn, date)).Sum(x => x.MonthlyContribution);

    /// <summary>Calculates debt payments due in the selected month.</summary>
    private decimal MonthlyDebtPayments(FinancialScenario scenario, DateOnly date) => scenario.Liabilities
        .Where(x => x.PaidOffOn is null || date <= x.PaidOffOn).Sum(x => ToBase(scenario, x.MonthlyPayment, x.Currency));

    /// <summary>Calculates the cash impact of life events that occur in the selected month.</summary>
    private static decimal MonthlyEventImpact(FinancialScenario scenario, DateOnly date) => scenario.Events.Sum(e =>
    {
        if (e.RecurrenceFrequency is { } frequency)
        {
            if (date < e.HappensOn || (e.RecurrenceEndsOn is { } endsOn && date > endsOn)) return 0m;
            return e.OneOffCashImpact * EventOccurrencesPerMonth(frequency, e.HappensOn, date);
        }

        var oneOff = SameMonth(e.HappensOn, date) ? e.OneOffCashImpact : 0m;
        var recurring = date >= e.HappensOn && (e.DurationMonths == 0 || date <= e.HappensOn.AddMonths(e.DurationMonths)) ? e.MonthlyCashImpact : 0m;
        return oneOff + recurring;
    });

    /// <summary>Converts an event recurrence into the expected occurrences in one projection month.</summary>
    private static decimal EventOccurrencesPerMonth(RecurrenceFrequency frequency, DateOnly startsOn, DateOnly date) => frequency switch
    {
        RecurrenceFrequency.Daily => DateTime.DaysInMonth(date.Year, date.Month),
        RecurrenceFrequency.Weekly => DateTime.DaysInMonth(date.Year, date.Month) / 7m,
        RecurrenceFrequency.EveryTwoWeeks => DateTime.DaysInMonth(date.Year, date.Month) / 14m,
        RecurrenceFrequency.Monthly => 1m,
        RecurrenceFrequency.Quarterly => MonthsBetween(startsOn, date) % 3 == 0 ? 1m : 0m,
        RecurrenceFrequency.Yearly => MonthsBetween(startsOn, date) % 12 == 0 ? 1m : 0m,
        RecurrenceFrequency.EveryFiveYears => MonthsBetween(startsOn, date) % 60 == 0 ? 1m : 0m,
        _ => 1m
    };

    /// <summary>Reserves one monthly contribution per planned one-off expense and releases it when the expense is due.</summary>
    private (decimal MonthlySavings, decimal DuePayments) ProcessSinkingFunds(
        FinancialScenario scenario,
        DateOnly date,
        IDictionary<Guid, decimal> balances)
    {
        var monthlySavings = 0m;
        var duePayments = 0m;
        foreach (var expense in scenario.Expenses.Where(item => item.Kind == ExpenseKind.Exceptional && item.SaveInAdvance))
        {
            var savingsStart = expense.SavingsStartsOn is { } configuredStart && configuredStart > scenario.StartsOn
                ? configuredStart
                : scenario.StartsOn;
            var totalMonths = MonthsBetween(savingsStart, expense.StartsOn) + 1;
            if (totalMonths > 0 && IsMonthInRange(date, savingsStart, expense.StartsOn))
            {
                var contribution = ToBase(scenario, expense.MonthlyAmount, expense.Currency) / totalMonths;
                balances.TryGetValue(expense.Id, out var currentBalance);
                balances[expense.Id] = currentBalance + contribution;
                monthlySavings += contribution;
            }

            if (!SameMonth(date, expense.StartsOn)) continue;
            var payment = ToBase(scenario, expense.MonthlyAmount, expense.Currency);
            duePayments += payment;
            balances.TryGetValue(expense.Id, out var savedAmount);
            balances[expense.Id] = Math.Max(0m, savedAmount - payment);
        }
        return (monthlySavings, duePayments);
    }

    /// <summary>Reduces outstanding debt by one monthly payment after applying a weighted monthly interest rate.</summary>
    private decimal AdvanceDebtMonthly(FinancialScenario scenario, decimal totalDebt, DateOnly date)
    {
        var active = scenario.Liabilities.Where(x => x.PaidOffOn is null || date <= x.PaidOffOn).ToArray();
        if (active.Length == 0) return 0m;
        var principal = active.Sum(x => ToBase(scenario, x.OutstandingBalance, x.Currency));
        var weightedRate = principal == 0 ? 0m : active.Sum(x => ToBase(scenario, x.OutstandingBalance, x.Currency) * x.InterestRate) / principal;
        return Math.Max(0m, totalDebt * (1m + weightedRate / 12m) - active.Sum(x => ToBase(scenario, x.MonthlyPayment, x.Currency)));
    }

    /// <summary>Combines declared pension income with estimates from all career periods.</summary>
    private decimal GetRetirementIncome(FinancialScenario scenario)
    {
        var publicPension = scenario.Profile!.Careers.Sum(x => x.EstimatedMonthlyPublicPension);
        var declaredPension = scenario.Incomes.Where(x => x.Kind == IncomeKind.Pension).Sum(x => ToBase(scenario, x.MonthlyAmount, x.Currency));
        return publicPension + declaredPension;
    }

    /// <summary>Groups current assets into base-currency allocation slices.</summary>
    private IReadOnlyList<AllocationSlice> BuildAllocation(FinancialScenario scenario)
    {
        var total = scenario.Assets.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency));
        return scenario.Assets.GroupBy(x => new { x.Name, x.Kind }).Select(x => new AllocationSlice(x.Key.Name, x.Key.Kind, x.Sum(y => ToBase(scenario, y.CurrentValue, y.Currency)), total == 0 ? 0 : Math.Round(x.Sum(y => ToBase(scenario, y.CurrentValue, y.Currency)) / total * 100m, 2))).OrderByDescending(x => x.Value).ToArray();
    }

    /// <summary>Builds clear warnings from solvency, purchasing power, liquidity, and debt indicators.</summary>
    private IReadOnlyList<string> BuildWarnings(FinancialScenario scenario, IReadOnlyList<ProjectionYear> timeline)
    {
        var warnings = new List<string>();
        var failure = timeline.Skip(1).FirstOrDefault(x => x.NetWorth < 0);
        if (failure is not null) warnings.Add($"You may run out of money at age {failure.Age}.");
        var first = timeline[0];
        var last = timeline[^1];
        if (first.NetWorth > 0 && last.InflationAdjustedNetWorth < first.NetWorth * 0.82m)
            warnings.Add($"Your purchasing power drops by {Math.Round((1m - last.InflationAdjustedNetWorth / first.NetWorth) * 100m):0}% in real terms.");
        if (scenario.Assets.Where(x => x.IsLiquid).Sum(x => ToBase(scenario, x.CurrentValue, x.Currency)) < scenario.Expenses.Sum(x => ToBase(scenario, x.MonthlyAmount, x.Currency)) * 3m)
            warnings.Add("Your liquid emergency fund is below three months of current expenses.");
        if (scenario.Liabilities.Sum(x => ToBase(scenario, x.MonthlyPayment, x.Currency)) > scenario.Incomes.Sum(x => ToBase(scenario, x.MonthlyAmount, x.Currency)) * 0.4m)
            warnings.Add("Debt payments exceed 40% of your declared monthly income.");
        return warnings;
    }

    /// <summary>Calculates a current-value-weighted return net of the declared capital-gains tax.</summary>
    private decimal WeightedReturn(IEnumerable<Asset> assets, FinancialScenario scenario)
    {
        var values = assets.ToArray();
        var total = values.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency));
        return total == 0 ? 0.04m : values.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency) * x.ExpectedAnnualReturn * (1m - Math.Clamp(x.CapitalGainsTaxRate, 0m, 1m))) / total;
    }

    /// <summary>Calculates a current-value-weighted volatility with a configurable fallback.</summary>
    private decimal WeightedVolatility(IEnumerable<Asset> assets, FinancialScenario scenario, decimal fallback)
    {
        var values = assets.ToArray();
        var total = values.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency));
        return total == 0 ? fallback : values.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency) * (x.Volatility == 0 ? fallback : x.Volatility)) / total;
    }

    /// <summary>Determines whether a dated entry is active for a projected date.</summary>
    private static bool IsActive(DateOnly startsOn, DateOnly? endsOn, DateOnly date) => startsOn <= date && (endsOn is null || endsOn >= date);

    /// <summary>Converts an amount to the owning profile's display currency.</summary>
    private decimal ToBase(FinancialScenario scenario, decimal amount, string currency) => _currencies.Convert(amount, currency, scenario.Profile!.BaseCurrency);

    /// <summary>Returns whole calendar years elapsed between two dates, never negative.</summary>
    private static int YearsBetween(DateOnly start, DateOnly end) => Math.Max(0, end.Year - start.Year);

    /// <summary>Returns calendar months elapsed between two dates, never negative.</summary>
    private static int MonthsBetween(DateOnly start, DateOnly end) => Math.Max(0, (end.Year - start.Year) * 12 + end.Month - start.Month);

    /// <summary>Checks whether two dates are part of the same calendar month.</summary>
    private static bool SameMonth(DateOnly left, DateOnly right) => left.Year == right.Year && left.Month == right.Month;

    /// <summary>Checks whether a date's month falls between two inclusive planning months.</summary>
    private static bool IsMonthInRange(DateOnly date, DateOnly startsOn, DateOnly endsOn) => MonthsBetween(startsOn, date) <= MonthsBetween(startsOn, endsOn) && date >= startsOn;

    /// <summary>Converts an annual rate into its compounded monthly equivalent.</summary>
    private static decimal MonthlyRate(decimal annualRate) => DecimalPow(1m + annualRate, 1m / 12m) - 1m;

    /// <summary>Raises a positive decimal value to a fractional power for monthly compounding.</summary>
    private static decimal DecimalPow(decimal value, decimal exponent) => value <= 0m ? 0m : (decimal)Math.Pow((double)value, (double)exponent);

    /// <summary>Raises a decimal value to a non-negative integer power without floating-point conversion.</summary>
    private static decimal Pow(decimal value, int exponent) { var output = 1m; for (var i = 0; i < exponent; i++) output *= value; return output; }

    /// <summary>Calculates age on a given date, accounting for whether the birthday has passed.</summary>
    private static int AgeAt(DateOnly birthDate, DateOnly date) => date.Year - birthDate.Year - (date < birthDate.AddYears(date.Year - birthDate.Year) ? 1 : 0);

    /// <summary>Generates a standard-normal random value using the Box-Muller transform.</summary>
    private static decimal Normal(Random random) { var u1 = 1d - random.NextDouble(); var u2 = 1d - random.NextDouble(); return (decimal)(Math.Sqrt(-2d * Math.Log(u1)) * Math.Cos(2d * Math.PI * u2)); }
}
