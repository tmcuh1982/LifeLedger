using LifeLedger.Api.Contracts;
using LifeLedger.Api.Domain;

namespace LifeLedger.Api.Services;

public sealed record ProjectionContext(FinancialScenario Scenario, int YearIndex, DateOnly Date, int Age, decimal InflationRate);

/// <summary>Extension point for country, tax and planning plugins to alter an annual projection.</summary>
public interface IProjectionModifier
{
    void Apply(ProjectionContext context, ProjectionAdjustment adjustment);
}

public sealed class ProjectionAdjustment
{
    public decimal IncomeDelta { get; set; }
    public decimal ExpenseDelta { get; set; }
    public decimal NetWorthDelta { get; set; }
    public List<string> Notes { get; } = [];
}

public interface IProjectionEngine
{
    SimulationResponse Simulate(FinancialScenario scenario, SimulationRequest request);
    DashboardResponse BuildDashboard(FinancialScenario scenario);
}

public sealed class ProjectionEngine(ICurrencyService currencies, IEnumerable<IProjectionModifier> modifiers) : IProjectionEngine
{
    private readonly ICurrencyService _currencies = currencies;
    private readonly IProjectionModifier[] _modifiers = modifiers.ToArray();
    private static readonly decimal[] HistoricalReturns = [0.18m, 0.12m, -0.21m, 0.08m, 0.16m, -0.11m, 0.23m, 0.06m, -0.07m, 0.14m, 0.09m, -0.18m];
    private static readonly decimal[] HistoricalInflation = [0.018m, 0.021m, 0.026m, 0.014m, 0.031m, 0.047m, 0.022m, 0.016m, 0.038m, 0.027m, 0.019m, 0.024m];

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

    private IReadOnlyList<ProjectionYear> Project(FinancialScenario scenario, int years, SimulationMode mode, Random? random)
    {
        var profile = scenario.Profile!;
        var assumptions = scenario.Assumptions;
        var startAge = AgeAt(profile.BirthDate, scenario.StartsOn);
        var assetValue = scenario.Assets.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency));
        var debt = scenario.Liabilities.Sum(x => ToBase(scenario, x.OutstandingBalance, x.Currency));
        var initialNetWorth = assetValue - debt;
        var weightedReturn = WeightedReturn(scenario.Assets, scenario);
        var weightedVolatility = WeightedVolatility(scenario.Assets, scenario, assumptions.DefaultReturnVolatility);
        var rows = new List<ProjectionYear>(years + 1);
        var inflationIndex = 1m;

        for (var year = 0; year <= years; year++)
        {
            var date = scenario.StartsOn.AddYears(year);
            var age = startAge + year;
            var annualInflation = mode == SimulationMode.Historical ? HistoricalInflation[year % HistoricalInflation.Length] : assumptions.InflationRate;
            if (year > 0) inflationIndex *= 1m + annualInflation;
            var income = AnnualIncome(scenario, date, age, assumptions.RetirementAge, assumptions.InflationRate);
            var expenses = AnnualExpenses(scenario, date, year, assumptions.InflationRate);
            var contributions = AnnualContributions(scenario, date);
            var debtPayment = AnnualDebtPayments(scenario, date);
            var eventImpact = AnnualEventImpact(scenario, date);
            var publicPension = age >= assumptions.RetirementAge ? GetRetirementIncome(scenario) * 12m : 0m;
            var passiveIncome = AnnualPassiveIncome(scenario, date, age, assumptions.RetirementAge);
            var adjustment = new ProjectionAdjustment();
            var context = new ProjectionContext(scenario, year, date, age, annualInflation);
            foreach (var modifier in _modifiers) modifier.Apply(context, adjustment);
            income += publicPension + adjustment.IncomeDelta;
            expenses += adjustment.ExpenseDelta;
            var cashFlow = income + passiveIncome - expenses - debtPayment - contributions + eventImpact;

            if (year > 0)
            {
                var annualReturn = mode switch
                {
                    SimulationMode.Historical => HistoricalReturns[year % HistoricalReturns.Length],
                    SimulationMode.MonteCarlo => weightedReturn + Normal(random!) * weightedVolatility,
                    _ => weightedReturn
                };
                assetValue = Math.Max(0m, assetValue * (1m + annualReturn) + cashFlow + contributions + adjustment.NetWorthDelta);
                debt = AdvanceDebt(scenario, debt, date);
            }

            var netWorth = assetValue - debt;
            rows.Add(new ProjectionYear(
                date.Year,
                age,
                Math.Round(netWorth, 2),
                Math.Round(cashFlow, 2),
                Math.Round(income + passiveIncome, 2),
                Math.Round(expenses + debtPayment + contributions, 2),
                Math.Round(passiveIncome, 2),
                Math.Round(netWorth / inflationIndex, 2)));
        }

        // Keep the current row precisely tied to what the user has entered.
        rows[0] = rows[0] with { NetWorth = initialNetWorth, InflationAdjustedNetWorth = initialNetWorth };
        return rows;
    }

    private decimal AnnualIncome(FinancialScenario scenario, DateOnly date, int age, int retirementAge, decimal inflationRate) => scenario.Incomes
        .Where(x => x.Kind is not (IncomeKind.Rental or IncomeKind.Dividends or IncomeKind.Royalties))
        .Where(x => IsActive(x.StartsOn, x.EndsOn, date) && (x.Kind != IncomeKind.Salary || age < retirementAge))
        .Sum(x =>
        {
            // Salary growth is expressed in real terms; headline inflation is added to preserve purchasing power.
            var growth = x.AnnualGrowthRate + (x.Kind == IncomeKind.Salary ? inflationRate : 0m);
            var grossIncome = x.MonthlyAmount * 12m * Pow(1m + growth, YearsBetween(x.StartsOn, date));
            return ToBase(scenario, AfterIncomeTax(x, grossIncome), x.Currency);
        });

    private decimal AnnualPassiveIncome(FinancialScenario scenario, DateOnly date, int age, int retirementAge) => scenario.Incomes
        .Where(x => x.Kind is IncomeKind.Rental or IncomeKind.Dividends or IncomeKind.Royalties)
        .Where(x => IsActive(x.StartsOn, x.EndsOn, date))
        .Sum(x => ToBase(scenario, AfterIncomeTax(x, x.MonthlyAmount * 12m * Pow(1m + x.AnnualGrowthRate, YearsBetween(x.StartsOn, date))), x.Currency));

    private static decimal AfterIncomeTax(IncomeStream income, decimal grossIncome) => income.IsTaxable
        ? grossIncome * (1m - Math.Clamp(income.TaxRate, 0m, 1m))
        : grossIncome;

    private decimal AnnualExpenses(FinancialScenario scenario, DateOnly date, int year, decimal inflationRate) => scenario.Expenses
        .Where(x => IsActive(x.StartsOn, x.EndsOn, date))
        .Sum(x => ToBase(scenario, x.MonthlyAmount * OccurrencesPerYear(x, date) * (x.IndexedToInflation ? Pow(1m + inflationRate, Math.Max(0, year)) : 1m), x.Currency));

    private static decimal OccurrencesPerYear(Expense expense, DateOnly date)
    {
        if (expense.Kind == ExpenseKind.Exceptional) return 1m;

        return expense.Frequency switch
        {
            RecurrenceFrequency.Daily => 365.25m,
            RecurrenceFrequency.Weekly => 52.18m,
            RecurrenceFrequency.EveryTwoWeeks => 26.09m,
            RecurrenceFrequency.Monthly => 12m,
            RecurrenceFrequency.Quarterly => 4m,
            RecurrenceFrequency.Yearly => 1m,
            RecurrenceFrequency.EveryFiveYears => (date.Year - expense.StartsOn.Year) % 5 == 0 ? 1m : 0m,
            _ => 12m
        };
    }

    private static decimal AnnualContributions(FinancialScenario scenario, DateOnly date) => scenario.Investments
        .Where(x => IsActive(x.StartsOn, x.EndsOn, date)).Sum(x => x.MonthlyContribution * 12m);

    private decimal AnnualDebtPayments(FinancialScenario scenario, DateOnly date) => scenario.Liabilities
        .Where(x => x.PaidOffOn is null || date <= x.PaidOffOn).Sum(x => ToBase(scenario, x.MonthlyPayment * 12m, x.Currency));

    private static decimal AnnualEventImpact(FinancialScenario scenario, DateOnly date) => scenario.Events.Sum(e =>
    {
        if (e.RecurrenceFrequency is { } frequency)
        {
            if (date < e.HappensOn || (e.RecurrenceEndsOn is { } endsOn && date > endsOn)) return 0m;
            return e.OneOffCashImpact * EventOccurrencesPerYear(frequency, e.HappensOn, date);
        }

        var oneOff = e.HappensOn.Year == date.Year ? e.OneOffCashImpact : 0m;
        var recurring = date >= e.HappensOn && (e.DurationMonths == 0 || date <= e.HappensOn.AddMonths(e.DurationMonths)) ? e.MonthlyCashImpact * 12m : 0m;
        return oneOff + recurring;
    });

    private static decimal EventOccurrencesPerYear(RecurrenceFrequency frequency, DateOnly startsOn, DateOnly date) => frequency switch
    {
        RecurrenceFrequency.Daily => 365.25m,
        RecurrenceFrequency.Weekly => 52.18m,
        RecurrenceFrequency.EveryTwoWeeks => 26.09m,
        RecurrenceFrequency.Monthly => 12m,
        RecurrenceFrequency.Quarterly => 4m,
        RecurrenceFrequency.Yearly => 1m,
        RecurrenceFrequency.EveryFiveYears => (date.Year - startsOn.Year) % 5 == 0 ? 1m : 0m,
        _ => 1m
    };

    private decimal AdvanceDebt(FinancialScenario scenario, decimal totalDebt, DateOnly date)
    {
        var active = scenario.Liabilities.Where(x => x.PaidOffOn is null || date <= x.PaidOffOn).ToArray();
        if (active.Length == 0) return 0m;
        var principal = active.Sum(x => ToBase(scenario, x.OutstandingBalance, x.Currency));
        var weightedRate = principal == 0 ? 0m : active.Sum(x => ToBase(scenario, x.OutstandingBalance, x.Currency) * x.InterestRate) / principal;
        return Math.Max(0m, totalDebt * (1m + weightedRate) - active.Sum(x => ToBase(scenario, x.MonthlyPayment * 12m, x.Currency)));
    }

    private decimal GetRetirementIncome(FinancialScenario scenario)
    {
        var publicPension = scenario.Profile!.Careers.Sum(x => x.EstimatedMonthlyPublicPension);
        var declaredPension = scenario.Incomes.Where(x => x.Kind == IncomeKind.Pension).Sum(x => ToBase(scenario, x.MonthlyAmount, x.Currency));
        return publicPension + declaredPension;
    }

    private IReadOnlyList<AllocationSlice> BuildAllocation(FinancialScenario scenario)
    {
        var total = scenario.Assets.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency));
        return scenario.Assets.GroupBy(x => new { x.Name, x.Kind }).Select(x => new AllocationSlice(x.Key.Name, x.Key.Kind, x.Sum(y => ToBase(scenario, y.CurrentValue, y.Currency)), total == 0 ? 0 : Math.Round(x.Sum(y => ToBase(scenario, y.CurrentValue, y.Currency)) / total * 100m, 2))).OrderByDescending(x => x.Value).ToArray();
    }

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

    private decimal WeightedReturn(IEnumerable<Asset> assets, FinancialScenario scenario)
    {
        var values = assets.ToArray();
        var total = values.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency));
        return total == 0 ? 0.04m : values.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency) * x.ExpectedAnnualReturn * (1m - Math.Clamp(x.CapitalGainsTaxRate, 0m, 1m))) / total;
    }

    private decimal WeightedVolatility(IEnumerable<Asset> assets, FinancialScenario scenario, decimal fallback)
    {
        var values = assets.ToArray();
        var total = values.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency));
        return total == 0 ? fallback : values.Sum(x => ToBase(scenario, x.CurrentValue, x.Currency) * (x.Volatility == 0 ? fallback : x.Volatility)) / total;
    }

    private static bool IsActive(DateOnly startsOn, DateOnly? endsOn, DateOnly date) => startsOn <= date && (endsOn is null || endsOn >= date);
    private decimal ToBase(FinancialScenario scenario, decimal amount, string currency) => _currencies.Convert(amount, currency, scenario.Profile!.BaseCurrency);
    private static int YearsBetween(DateOnly start, DateOnly end) => Math.Max(0, end.Year - start.Year);
    private static decimal Pow(decimal value, int exponent) { var output = 1m; for (var i = 0; i < exponent; i++) output *= value; return output; }
    private static int AgeAt(DateOnly birthDate, DateOnly date) => date.Year - birthDate.Year - (date < birthDate.AddYears(date.Year - birthDate.Year) ? 1 : 0);
    private static decimal Normal(Random random) { var u1 = 1d - random.NextDouble(); var u2 = 1d - random.NextDouble(); return (decimal)(Math.Sqrt(-2d * Math.Log(u1)) * Math.Cos(2d * Math.PI * u2)); }
}
