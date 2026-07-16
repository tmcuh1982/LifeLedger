using LifeLedger.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace LifeLedger.Api.Data;

public static class DemoDataSeeder
{
    public static async Task SeedAsync(LifeLedgerDbContext db, CancellationToken cancellationToken = default)
    {
        if (await db.Profiles.AnyAsync(cancellationToken)) return;

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var profile = new Profile
        {
            DisplayName = "Your plan",
            BirthDate = today.AddYears(-36),
            HomeCountryCode = "PL",
            BaseCurrency = "EUR",
            ExpectedLifespan = 92,
            PartnerBirthYear = today.Year - 35,
            ChildrenCount = 1,
            Careers =
            [
                new CareerPeriod { CountryCode = "PL", StartedOn = today.AddYears(-14), AnnualInsurableIncome = 52000, EstimatedMonthlyPublicPension = 1050, PensionAge = 65 },
                new CareerPeriod { CountryCode = "BE", StartedOn = today.AddYears(-4), AnnualInsurableIncome = 72000, EstimatedMonthlyPublicPension = 420, PensionAge = 67 }
            ]
        };

        var scenario = new FinancialScenario
        {
            Profile = profile,
            Name = "Continue as planned",
            Description = "A private local sample plan. Replace it with your own figures.",
            IsBaseline = true,
            StartsOn = today,
            Assumptions = new SimulationAssumptions { InflationRate = 0.025m, SalaryGrowthRate = 0.02m, RetirementAge = 65, MonteCarloRuns = 600 },
            Incomes =
            [
                new IncomeStream { Name = "Salary", Kind = IncomeKind.Salary, MonthlyAmount = 5200, AnnualGrowthRate = 0.02m, StartsOn = today, Currency = "EUR" },
                new IncomeStream { Name = "Rental income", Kind = IncomeKind.Rental, MonthlyAmount = 650, AnnualGrowthRate = 0.015m, StartsOn = today, Currency = "EUR" }
            ],
            Assets =
            [
                new Asset { Name = "Cash reserve", Kind = AssetKind.Cash, CurrentValue = 25000, ExpectedAnnualReturn = 0.015m, Volatility = 0.01m, Currency = "EUR" },
                new Asset { Name = "Global ETF", Kind = AssetKind.Etf, CurrentValue = 105000, ExpectedAnnualReturn = 0.065m, Volatility = 0.15m, Currency = "EUR" },
                new Asset { Name = "Home equity", Kind = AssetKind.RealEstate, CurrentValue = 85000, ExpectedAnnualReturn = 0.025m, Volatility = 0.06m, IsLiquid = false, Currency = "EUR" }
            ],
            Liabilities =
            [
                new Liability { Name = "Mortgage", Kind = LiabilityKind.Mortgage, OutstandingBalance = 185000, InterestRate = 0.038m, MonthlyPayment = 1150, PaidOffOn = today.AddYears(19), Currency = "EUR" }
            ],
            Expenses =
            [
                new Expense { Name = "Living costs", Kind = ExpenseKind.Recurring, MonthlyAmount = 2800, IndexedToInflation = true, StartsOn = today, Currency = "EUR" },
                new Expense { Name = "Family & travel", Kind = ExpenseKind.Recurring, MonthlyAmount = 600, IndexedToInflation = true, StartsOn = today, Currency = "EUR" }
            ],
            Investments =
            [
                new InvestmentPlan { Name = "Monthly ETF investment", MonthlyContribution = 1200, ExpectedAnnualReturn = 0.065m, StartsOn = today }
            ],
            Events =
            [
                new ScenarioEvent { Name = "Kitchen renovation", Kind = EventKind.HousePurchase, HappensOn = today.AddYears(3), OneOffCashImpact = -30000, Notes = "Planned home renovation" },
                new ScenarioEvent { Name = "Salary review", Kind = EventKind.SalaryIncrease, HappensOn = today.AddYears(2), MonthlyCashImpact = 500, DurationMonths = 0, Notes = "Expected promotion" }
            ]
        };

        db.Scenarios.Add(scenario);
        await db.SaveChangesAsync(cancellationToken);
    }
}
