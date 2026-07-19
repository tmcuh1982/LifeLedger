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
public sealed class ProjectionEngine(ICurrencyService currencies, IIncomeScheduleService incomeSchedules, IExpenseScheduleService expenseSchedules, IEnumerable<IProjectionModifier> modifiers) : IProjectionEngine
{
    /// <summary>Converts each source amount to the profile's base currency.</summary>
    private readonly ICurrencyService _currencies = currencies;
    /// <summary>Converts income declarations into the amount received in each projected month.</summary>
    private readonly IIncomeScheduleService _incomeSchedules = incomeSchedules;
    /// <summary>Applies explicit future expense steps and inflation without double counting either.</summary>
    private readonly IExpenseScheduleService _expenseSchedules = expenseSchedules;
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
            .Where(x => IsActive(x.StartsOn, x.EndsOn, scenario.StartsOn))
            .Sum(x => ToBase(scenario, AfterIncomeTax(x, _incomeSchedules.GrossAnnualAmount(x, scenario.StartsOn)) / 12m, x.Currency));
        // Expected asset appreciation is shown separately because it increases wealth but is not spendable cash income.
        var expectedMonthlyPortfolioGrowth = scenario.Assets.Sum(asset =>
            OwnedAssetValue(scenario, asset) * asset.ExpectedAnnualReturn * (1m - Math.Clamp(asset.CapitalGainsTaxRate, 0m, 1m))) / 12m;
        // Financial independence is reached when the planned withdrawal rate covers annual expenses.
        // The initial snapshot has no annual expense total, so independence is evaluated from the first complete projected year.
        var fi = timeline.Skip(1).FirstOrDefault(x => x.NetWorth * scenario.Assumptions.SafeWithdrawalRate >= x.Expenses)?.Year;
        DateOnly? fiDate = fi is null ? null : scenario.StartsOn.AddYears(Math.Max(0, fi.Value - scenario.StartsOn.Year));
        var purchasingPowerChange = first.NetWorth == 0 ? 0 : (last.InflationAdjustedNetWorth / first.NetWorth - 1m) * 100m;
        var allocation = BuildAllocation(scenario);
        var allocationStrategy = AssessAllocationStrategy(scenario, allocation);
        var warnings = deterministic.Warnings.Concat(monteCarlo.Warnings).Distinct().Take(5).ToArray();

        return new DashboardResponse(
            scenario.Id,
            scenario.Name,
            scenario.Profile!.BaseCurrency,
            first.NetWorth,
            last.NetWorth,
            passiveIncome,
            expectedMonthlyPortfolioGrowth,
            retirementIncome,
            fiDate,
            purchasingPowerChange,
            monteCarlo.ProbabilityOfSuccess,
            timeline,
            allocation,
            allocationStrategy,
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
        if (probability < 0.8m) warnings.Add(new SimulationWarning("low-monte-carlo-success", Math.Round(probability * 100m)));
        return new SimulationResponse(SimulationMode.MonteCarlo, runs, probability, deterministicTimeline, terminalValues.Order().ToArray(), warnings.Distinct().ToArray());
    }

    /// <summary>Projects monthly cash flow internally and returns one clear summary row for each future year.</summary>
    private IReadOnlyList<ProjectionYear> Project(FinancialScenario scenario, int years, SimulationMode mode, Random? random)
    {
        var profile = scenario.Profile!;
        var assumptions = scenario.Assumptions;
        // All calculations are consolidated in the profile's base currency before aggregation.
        var assetValues = scenario.Assets.ToDictionary(asset => asset.Id, asset => OwnedAssetValue(scenario, asset));
        var investmentValues = scenario.Investments.ToDictionary(plan => plan.Id, _ => 0m);
        var projectedCash = 0m;
        var debtValues = scenario.Liabilities.ToDictionary(liability => liability.Id, liability => PersonalDebtValue(scenario, liability));
        var initialDebt = debtValues.Values.Sum();
        var initialNetWorth = assetValues.Values.Sum() - initialDebt;
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
            0m,
            BuildWealthComponents(scenario, assetValues, investmentValues, projectedCash, initialDebt, sinkingFundBalances, Math.Round(initialNetWorth, 2)),
            []));

        for (var year = 1; year <= years; year++)
        {
            var annualInflation = mode == SimulationMode.Historical ? HistoricalInflation[(year - 1) % HistoricalInflation.Length] : assumptions.InflationRate;
            var portfolioAnnualReturn = mode switch
            {
                SimulationMode.Historical => HistoricalReturns[(year - 1) % HistoricalReturns.Length],
                SimulationMode.MonteCarlo => weightedReturn + Normal(random!) * weightedVolatility,
                _ => weightedReturn
            };
            // One common market shock retains the previous correlated Monte Carlo behaviour while each asset keeps its own return and volatility.
            var marketShock = mode == SimulationMode.MonteCarlo && weightedVolatility > 0m
                ? (portfolioAnnualReturn - weightedReturn) / weightedVolatility
                : 0m;
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
            var appliedAssetSales = new List<ProjectedAssetSale>();
            for (var month = 0; month < 12; month++)
            {
                var date = scenario.StartsOn.AddMonths((year - 1) * 12 + month);
                var age = AgeAt(profile.BirthDate, date);
                var monthlyIncome = MonthlyIncome(scenario, date, age, assumptions.RetirementAge, assumptions.InflationRate);
                var monthlyPassiveIncome = MonthlyPassiveIncome(scenario, date);
                var monthlyPublicPension = age >= assumptions.RetirementAge ? GetRetirementIncome(scenario) : 0m;
                var monthlyExpenses = MonthlyExpenses(scenario, date, assumptions.InflationRate);
                var monthlyContributions = MonthlyContributions(scenario, date);
                var monthlyDebtPayment = MonthlyDebtPayments(scenario, date, debtValues);
                var monthlyEventImpact = MonthlyEventImpact(scenario, date);
                var (monthlySavings, _) = ProcessSinkingFunds(scenario, date, sinkingFundBalances);
                var monthlyCashFlow = monthlyIncome + monthlyPassiveIncome + monthlyPublicPension + adjustment.IncomeDelta / 12m
                    - monthlyExpenses - monthlyDebtPayment - monthlyContributions - monthlySavings - adjustment.ExpenseDelta / 12m + monthlyEventImpact;

                // Existing assets compound independently so property, ETFs and cash remain visible as distinct balance-sheet components.
                foreach (var asset in scenario.Assets)
                {
                    var monthlyAssetReturn = MonthlyRate(AnnualAssetReturn(asset, mode, year, marketShock, assumptions.DefaultReturnVolatility));
                    if (assetValues[asset.Id] > 0m) assetValues[asset.Id] += assetValues[asset.Id] * monthlyAssetReturn;
                }

                // Contributions move from cash flow into their own invested balance; sinking-fund transfers are already held in their dedicated balances.
                foreach (var plan in scenario.Investments.Where(plan => IsActive(plan.StartsOn, plan.EndsOn, date)))
                {
                    var annualPlanReturn = mode == SimulationMode.Historical
                        ? HistoricalReturns[(year - 1) % HistoricalReturns.Length]
                        : plan.ExpectedAnnualReturn + (mode == SimulationMode.MonteCarlo ? marketShock * assumptions.DefaultReturnVolatility : 0m);
                    if (investmentValues[plan.Id] > 0m) investmentValues[plan.Id] += investmentValues[plan.Id] * MonthlyRate(annualPlanReturn);
                    investmentValues[plan.Id] += plan.MonthlyContribution;
                }

                // Unassigned surpluses and deficits stay in projected cash and never masquerade as property or market appreciation.
                projectedCash += monthlyCashFlow + (month == 11 ? adjustment.NetWorthDelta : 0m);
                AdvanceDebtsMonthly(scenario, debtValues, date);
                appliedAssetSales.AddRange(ProcessAssetSales(scenario, date, assetValues, investmentValues, debtValues, ref projectedCash));
                CoverCashDeficit(scenario, assetValues, investmentValues, ref projectedCash);
                inflationIndex *= 1m + monthlyInflation;

                cashFlow += monthlyCashFlow;
                income += monthlyIncome + monthlyPassiveIncome + monthlyPublicPension + adjustment.IncomeDelta / 12m;
                expenses += monthlyExpenses + monthlyDebtPayment + monthlyContributions + monthlySavings + adjustment.ExpenseDelta / 12m;
                passiveIncome += monthlyPassiveIncome;
                plannedSavings += monthlySavings;
            }

            var debt = debtValues.Values.Sum();
            var netWorth = assetValues.Values.Sum() + investmentValues.Values.Sum() + projectedCash + sinkingFundBalances.Values.Sum() - debt;
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
                Math.Round(sinkingFundBalances.Values.Sum(), 2),
                BuildWealthComponents(scenario, assetValues, investmentValues, projectedCash, debt, sinkingFundBalances, Math.Round(netWorth, 2)),
                appliedAssetSales));
        }

        return rows;
    }

    /// <summary>Calculates non-passive income received in one month after growth, inflation indexing, and declared tax.</summary>
    private decimal MonthlyIncome(FinancialScenario scenario, DateOnly date, int age, int retirementAge, decimal inflationRate) => scenario.Incomes
        .Where(x => x.Kind is not (IncomeKind.Rental or IncomeKind.Dividends or IncomeKind.Royalties or IncomeKind.Pension))
        .Where(x => IsActive(x.StartsOn, x.EndsOn, date) && (x.Kind != IncomeKind.Salary || age < retirementAge))
        .Sum(x =>
        {
            // Salary growth is expressed in real terms; headline inflation is added to preserve purchasing power.
            var grossIncome = _incomeSchedules.GrossAmountForMonth(x, date, x.Kind == IncomeKind.Salary ? inflationRate : 0m);
            return ToBase(scenario, AfterIncomeTax(x, grossIncome), x.Currency);
        });

    /// <summary>Calculates rental, dividend, and royalty income received in one month after declared tax.</summary>
    private decimal MonthlyPassiveIncome(FinancialScenario scenario, DateOnly date) => scenario.Incomes
        .Where(x => x.Kind is IncomeKind.Rental or IncomeKind.Dividends or IncomeKind.Royalties)
        .Where(x => IsActive(x.StartsOn, x.EndsOn, date))
        .Sum(x => ToBase(scenario, AfterIncomeTax(x, _incomeSchedules.GrossAmountForMonth(x, date)), x.Currency));

    /// <summary>Applies an effective tax rate clamped to a valid fraction.</summary>
    private static decimal AfterIncomeTax(IncomeStream income, decimal grossIncome) => income.IsTaxable
        ? grossIncome * (1m - Math.Clamp(income.TaxRate, 0m, 1m))
        : grossIncome;

    /// <summary>Calculates ordinary expenses for one month; one-off expenses are charged only in their due month.</summary>
    private decimal MonthlyExpenses(FinancialScenario scenario, DateOnly date, decimal inflationRate) => scenario.Expenses
        .Where(expense => expense.Kind == ExpenseKind.Recurring
            ? IsActive(expense.StartsOn, expense.EndsOn, date)
            : SameMonth(expense.StartsOn, date) && !expense.SaveInAdvance)
        .Sum(expense => ToBase(
            scenario,
            _expenseSchedules.AmountForOccurrence(expense, date, inflationRate) * OccurrencesPerMonth(expense, date),
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

    /// <summary>Calculates debt payments due in the selected month without charging liabilities already settled by a sale.</summary>
    private decimal MonthlyDebtPayments(FinancialScenario scenario, DateOnly date, IReadOnlyDictionary<Guid, decimal> debtValues) => scenario.Liabilities
        .Where(liability => (liability.PaidOffOn is null || date <= liability.PaidOffOn) && debtValues.GetValueOrDefault(liability.Id) > 0m)
        .Sum(liability => Math.Min(
            debtValues.GetValueOrDefault(liability.Id) * (1m + Math.Max(0m, liability.InterestRate) / 12m),
            PersonalDebtPayment(scenario, liability)));

    /// <summary>Calculates the cash impact of life events that occur in the selected month.</summary>
    private decimal MonthlyEventImpact(FinancialScenario scenario, DateOnly date) => scenario.Events.Sum(e =>
    {
        if (e.RecurrenceFrequency is { } frequency)
        {
            if (date < e.HappensOn || (e.RecurrenceEndsOn is { } endsOn && date > endsOn)) return 0m;
            return ToBase(scenario, e.OneOffCashImpact * EventOccurrencesPerMonth(frequency, e.HappensOn, date), e.Currency);
        }

        var oneOff = SameMonth(e.HappensOn, date) ? e.OneOffCashImpact : 0m;
        var recurring = date >= e.HappensOn && (e.DurationMonths == 0 || date <= e.HappensOn.AddMonths(e.DurationMonths)) ? e.MonthlyCashImpact : 0m;
        return ToBase(scenario, oneOff + recurring, e.Currency);
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

    /// <summary>Advances every liability independently so a sale can settle only debt linked to its asset.</summary>
    private void AdvanceDebtsMonthly(FinancialScenario scenario, IDictionary<Guid, decimal> debtValues, DateOnly date)
    {
        foreach (var liability in scenario.Liabilities)
        {
            debtValues.TryGetValue(liability.Id, out var balance);
            if (balance <= 0m || liability.PaidOffOn is { } paidOffOn && date > paidOffOn)
            {
                debtValues[liability.Id] = 0m;
                continue;
            }

            var payment = PersonalDebtPayment(scenario, liability);
            debtValues[liability.Id] = Math.Max(0m, balance * (1m + Math.Max(0m, liability.InterestRate) / 12m) - payment);
        }
    }

    /// <summary>Applies every sale due in the selected month and returns a transparent breakdown for the annual timeline.</summary>
    private IReadOnlyList<ProjectedAssetSale> ProcessAssetSales(
        FinancialScenario scenario,
        DateOnly date,
        IDictionary<Guid, decimal> assetValues,
        IDictionary<Guid, decimal> investmentValues,
        IDictionary<Guid, decimal> debtValues,
        ref decimal projectedCash)
    {
        var results = new List<ProjectedAssetSale>();
        foreach (var sale in scenario.AssetSales.Where(candidate => SameMonth(candidate.HappensOn, date)))
        {
            var asset = scenario.Assets.SingleOrDefault(candidate => candidate.Id == sale.AssetId);
            if (asset is null || !assetValues.TryGetValue(asset.Id, out var projectedAssetValue) || projectedAssetValue <= 0m) continue;

            var grossProceeds = sale.UseProjectedValue ? projectedAssetValue : ToBase(scenario, sale.GrossSalePrice, sale.Currency);
            var sellingCosts = ToBase(scenario, sale.SellingCosts, sale.Currency);
            var acquisitionBasis = ToBase(scenario, (asset.PurchasePrice + asset.AcquisitionCosts) * asset.OwnershipRate, asset.Currency);
            var capitalGain = Math.Max(0m, grossProceeds - acquisitionBasis);
            var capitalGainsTax = capitalGain * Math.Clamp(sale.CapitalGainsTaxRate, 0m, 1m);
            var remainingProceeds = grossProceeds - sellingCosts - capitalGainsTax;
            var debtRepaid = 0m;

            if (sale.RepayLinkedLiabilities && remainingProceeds > 0m)
            {
                foreach (var link in asset.LiabilityLinks.OrderByDescending(link => link.AllocationRate))
                {
                    debtValues.TryGetValue(link.LiabilityId, out var outstanding);
                    var allocatedOutstanding = outstanding * Math.Clamp(link.AllocationRate, 0m, 1m);
                    var repayment = Math.Min(remainingProceeds, allocatedOutstanding);
                    if (repayment <= 0m) continue;
                    debtValues[link.LiabilityId] = outstanding - repayment;
                    remainingProceeds -= repayment;
                    debtRepaid += repayment;
                }
            }

            // Removing the asset and adding its proceeds is a transfer; only price differences, fees and tax change net worth.
            assetValues[asset.Id] = 0m;
            if (remainingProceeds < 0m || sale.Destination == AssetSaleDestination.Cash)
                projectedCash += remainingProceeds;
            else if (sale.Destination == AssetSaleDestination.Asset && sale.DestinationAssetId is { } destinationAssetId && assetValues.ContainsKey(destinationAssetId))
                assetValues[destinationAssetId] += remainingProceeds;
            else if (sale.Destination == AssetSaleDestination.InvestmentPlan && sale.DestinationInvestmentPlanId is { } destinationPlanId && investmentValues.ContainsKey(destinationPlanId))
                investmentValues[destinationPlanId] += remainingProceeds;
            else
                projectedCash += remainingProceeds;

            results.Add(new ProjectedAssetSale(
                sale.Id,
                asset.Id,
                sale.Name,
                sale.HappensOn,
                Math.Round(grossProceeds, 2),
                Math.Round(sellingCosts, 2),
                Math.Round(capitalGainsTax, 2),
                Math.Round(debtRepaid, 2),
                Math.Round(remainingProceeds, 2),
                scenario.Profile!.BaseCurrency,
                sale.Destination));
        }
        return results;
    }

    /// <summary>Combines declared pension income with estimates from all career periods.</summary>
    private decimal GetRetirementIncome(FinancialScenario scenario)
    {
        var publicPension = scenario.Profile!.Careers.Sum(x => x.EstimatedMonthlyPublicPension);
        var declaredPension = scenario.Incomes.Where(x => x.Kind == IncomeKind.Pension)
            .Sum(x => ToBase(scenario, _incomeSchedules.GrossAnnualAmount(x, scenario.StartsOn) / 12m, x.Currency));
        return publicPension + declaredPension;
    }

    /// <summary>Returns the annual return applied to one owned asset without blending it with unrelated categories.</summary>
    private static decimal AnnualAssetReturn(Asset asset, SimulationMode mode, int year, decimal marketShock, decimal fallbackVolatility)
    {
        var expectedNetReturn = asset.ExpectedAnnualReturn * (1m - Math.Clamp(asset.CapitalGainsTaxRate, 0m, 1m));
        var annualReturn = mode switch
        {
            // Cash keeps its declared rate because a stock-market history must not make a bank balance rise or fall.
            SimulationMode.Historical when asset.Kind == AssetKind.Cash => expectedNetReturn,
            SimulationMode.Historical => HistoricalReturns[(year - 1) % HistoricalReturns.Length],
            SimulationMode.MonteCarlo => expectedNetReturn + marketShock * AssetVolatility(asset, fallbackVolatility),
            _ => expectedNetReturn
        };
        return Math.Max(-0.9999m, annualReturn);
    }

    /// <summary>Builds an exact category-level reconciliation of projected assets, reserves, cash and debt.</summary>
    private static IReadOnlyList<ProjectionWealthComponent> BuildWealthComponents(
        FinancialScenario scenario,
        IReadOnlyDictionary<Guid, decimal> assetValues,
        IReadOnlyDictionary<Guid, decimal> investmentValues,
        decimal projectedCash,
        decimal debt,
        IReadOnlyDictionary<Guid, decimal> sinkingFundBalances,
        decimal roundedNetWorth)
    {
        var components = scenario.Assets
            .GroupBy(asset => string.IsNullOrWhiteSpace(asset.CustomCategory) ? asset.Kind.ToString() : asset.CustomCategory.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var kinds = group.Select(asset => asset.Kind).Distinct().ToArray();
                var category = group.First().CustomCategory?.Trim() is { Length: > 0 } customCategory ? customCategory : group.Key;
                var key = $"asset:{group.Key.Trim().ToLowerInvariant()}";
                var value = group.Sum(asset => assetValues.GetValueOrDefault(asset.Id));
                return new ProjectionWealthComponent(key, category, kinds.Length == 1 ? kinds[0] : null, ProjectionWealthComponentType.Asset, Math.Round(value, 2));
            })
            .OrderByDescending(component => component.Value)
            .ToList();

        // System components stay present at zero so every timeline row exposes the same stable chart series.
        components.Add(new ProjectionWealthComponent("system:investments", "InvestmentPlans", null, ProjectionWealthComponentType.Investment, Math.Round(investmentValues.Values.Sum(), 2)));
        components.Add(new ProjectionWealthComponent("system:projected-cash", "ProjectedCash", AssetKind.Cash, ProjectionWealthComponentType.ProjectedCash, Math.Round(projectedCash, 2)));
        components.Add(new ProjectionWealthComponent("system:planned-reserve", "PlannedExpenseReserve", AssetKind.Cash, ProjectionWealthComponentType.PlannedExpenseReserve, Math.Round(sinkingFundBalances.Values.Sum(), 2)));
        components.Add(new ProjectionWealthComponent("system:liabilities", "Liabilities", null, ProjectionWealthComponentType.Liability, Math.Round(-debt, 2)));
        // Assign any cent-level component rounding difference to projected cash so the displayed stack reconciles exactly to the displayed total.
        var cashIndex = components.FindIndex(component => component.Type == ProjectionWealthComponentType.ProjectedCash);
        var roundingDifference = roundedNetWorth - components.Sum(component => component.Value);
        components[cashIndex] = components[cashIndex] with { Value = components[cashIndex].Value + roundingDifference };
        return components;
    }

    /// <summary>Covers a cash shortfall by selling cash assets, liquid investments and finally non-liquid assets in that order.</summary>
    private static void CoverCashDeficit(
        FinancialScenario scenario,
        IDictionary<Guid, decimal> assetValues,
        IDictionary<Guid, decimal> investmentValues,
        ref decimal projectedCash)
    {
        if (projectedCash >= 0m) return;

        // Cash and readily saleable holdings fund normal spending before property or other non-liquid wealth is touched.
        var liquidationOrder = scenario.Assets
            .OrderBy(asset => asset.Kind == AssetKind.Cash ? 0 : asset.IsLiquid ? 1 : 3)
            .ToArray();
        foreach (var asset in liquidationOrder.Where(asset => asset.Kind == AssetKind.Cash || asset.IsLiquid))
            LiquidateBalance(assetValues, asset.Id, ref projectedCash);

        foreach (var plan in scenario.Investments)
            LiquidateBalance(investmentValues, plan.Id, ref projectedCash);

        foreach (var asset in liquidationOrder.Where(asset => asset.Kind != AssetKind.Cash && !asset.IsLiquid))
            LiquidateBalance(assetValues, asset.Id, ref projectedCash);
    }

    /// <summary>Transfers only the amount required from one projected balance into cash without changing total wealth.</summary>
    private static void LiquidateBalance(IDictionary<Guid, decimal> balances, Guid key, ref decimal projectedCash)
    {
        if (projectedCash >= 0m || !balances.TryGetValue(key, out var available) || available <= 0m) return;
        var amount = Math.Min(available, -projectedCash);
        balances[key] = available - amount;
        projectedCash += amount;
    }

    /// <summary>Groups included assets by user-defined allocation category, falling back to their technical asset kind.</summary>
    private IReadOnlyList<AllocationSlice> BuildAllocation(FinancialScenario scenario)
    {
        var assets = scenario.Assets.Where(asset => asset.IsIncludedInPortfolioAllocation).ToArray();
        var total = assets.Sum(x => OwnedAssetValue(scenario, x));
        return assets.GroupBy(asset => string.IsNullOrWhiteSpace(asset.CustomCategory) ? asset.Kind.ToString() : asset.CustomCategory.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(group => new AllocationSlice(group.Key, group.Select(asset => asset.Kind).Distinct().Count() == 1 ? group.First().Kind : AssetKind.Other, group.Sum(asset => OwnedAssetValue(scenario, asset)), total == 0 ? 0 : Math.Round(group.Sum(asset => OwnedAssetValue(scenario, asset)) / total * 100m, 2)))
            .OrderByDescending(slice => slice.Value).ToArray();
    }

    /// <summary>Calculates category drift against the one strategy version active today without changing the strategy or portfolio.</summary>
    private static AllocationStrategyAssessment? AssessAllocationStrategy(FinancialScenario scenario, IReadOnlyList<AllocationSlice> allocation)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var strategy = scenario.AllocationStrategies.SingleOrDefault(candidate => candidate.EffectiveFrom <= today && (candidate.EffectiveTo is null || candidate.EffectiveTo >= today));
        if (strategy is null) return null;
        var actual = allocation.ToDictionary(slice => slice.Name, slice => slice.Percentage, StringComparer.OrdinalIgnoreCase);
        var targets = strategy.Targets.OrderByDescending(target => target.TargetPercentage).Select(target =>
        {
            var actualPercentage = actual.GetValueOrDefault(target.Category);
            var difference = Math.Round(actualPercentage - target.TargetPercentage, 2);
            var state = difference < -target.TolerancePercentage ? AllocationTargetState.Underweight
                : difference > target.TolerancePercentage ? AllocationTargetState.Overweight
                : AllocationTargetState.WithinRange;
            return new AllocationTargetAssessment(target.Category, target.TargetPercentage, target.TolerancePercentage, actualPercentage, difference, state);
        }).ToArray();
        return new AllocationStrategyAssessment(strategy.Name, strategy.EffectiveFrom, strategy.EffectiveTo, strategy.Targets.Sum(target => target.TargetPercentage), targets);
    }

    /// <summary>Builds clear warnings from solvency, purchasing power, liquidity, and debt indicators.</summary>
    private IReadOnlyList<SimulationWarning> BuildWarnings(FinancialScenario scenario, IReadOnlyList<ProjectionYear> timeline)
    {
        var warnings = new List<SimulationWarning>();
        var failure = timeline.Skip(1).FirstOrDefault(x => x.NetWorth < 0);
        if (failure is not null) warnings.Add(new SimulationWarning("insolvency-age", failure.Age));
        var first = timeline[0];
        var last = timeline[^1];
        if (first.NetWorth > 0 && last.InflationAdjustedNetWorth < first.NetWorth * 0.82m)
            warnings.Add(new SimulationWarning("purchasing-power-drop", Math.Round((1m - last.InflationAdjustedNetWorth / first.NetWorth) * 100m)));
        if (scenario.Assets.Where(x => x.IsLiquid).Sum(x => OwnedAssetValue(scenario, x)) < scenario.Expenses.Sum(x => ToBase(scenario, _expenseSchedules.AmountForOccurrence(x, scenario.StartsOn, scenario.Assumptions.InflationRate) * OccurrencesPerMonth(x, scenario.StartsOn), x.Currency)) * 3m)
            warnings.Add(new SimulationWarning("low-emergency-fund"));
        var averageMonthlyIncome = scenario.Incomes.Sum(x => ToBase(scenario, _incomeSchedules.GrossAnnualAmount(x, scenario.StartsOn) / 12m, x.Currency));
        if (scenario.Liabilities.Sum(x => PersonalDebtPayment(scenario, x)) > averageMonthlyIncome * 0.4m)
            warnings.Add(new SimulationWarning("high-debt-payments"));
        return warnings;
    }

    /// <summary>Calculates a current-value-weighted return net of the declared capital-gains tax.</summary>
    private decimal WeightedReturn(IEnumerable<Asset> assets, FinancialScenario scenario)
    {
        var values = assets.ToArray();
        var total = values.Sum(x => OwnedAssetValue(scenario, x));
        return total == 0 ? 0m : values.Sum(x => OwnedAssetValue(scenario, x) * x.ExpectedAnnualReturn * (1m - Math.Clamp(x.CapitalGainsTaxRate, 0m, 1m))) / total;
    }

    /// <summary>Calculates a current-value-weighted volatility with a configurable fallback.</summary>
    private decimal WeightedVolatility(IEnumerable<Asset> assets, FinancialScenario scenario, decimal fallback)
    {
        var values = assets.ToArray();
        var total = values.Sum(x => OwnedAssetValue(scenario, x));
        return total == 0 ? 0m : values.Sum(x => OwnedAssetValue(scenario, x) * AssetVolatility(x, fallback)) / total;
    }

    /// <summary>Preserves zero volatility for cash while retaining the configured fallback for other unconfigured assets.</summary>
    private static decimal AssetVolatility(Asset asset, decimal fallback) => asset.Kind == AssetKind.Cash ? 0m : asset.Volatility == 0m ? fallback : asset.Volatility;

    /// <summary>Determines whether a dated entry is active for a projected date.</summary>
    private static bool IsActive(DateOnly startsOn, DateOnly? endsOn, DateOnly date) => startsOn <= date && (endsOn is null || endsOn >= date);

    /// <summary>Converts an amount to the owning profile's display currency.</summary>
    private decimal ToBase(FinancialScenario scenario, decimal amount, string currency) => _currencies.Convert(amount, currency, scenario.Profile!.BaseCurrency);

    /// <summary>Returns only the profile's economic share of an asset in the profile currency.</summary>
    private decimal OwnedAssetValue(FinancialScenario scenario, Asset asset) =>
        ToBase(scenario, asset.CurrentValue * Math.Clamp(asset.OwnershipRate, 0m, 1m), asset.Currency);

    /// <summary>Returns only the outstanding debt for which the profile is personally responsible.</summary>
    private decimal PersonalDebtValue(FinancialScenario scenario, Liability liability) =>
        ToBase(scenario, liability.OutstandingBalance * Math.Clamp(liability.ResponsibilityRate, 0m, 1m), liability.Currency);

    /// <summary>Returns the profile's personal share of the contractual monthly debt payment.</summary>
    private decimal PersonalDebtPayment(FinancialScenario scenario, Liability liability) =>
        ToBase(scenario, liability.MonthlyPayment * Math.Clamp(liability.ResponsibilityRate, 0m, 1m), liability.Currency);

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
