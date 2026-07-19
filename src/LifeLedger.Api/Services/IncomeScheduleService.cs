using LifeLedger.Api.Domain;

namespace LifeLedger.Api.Services;

/// <summary>Converts monthly, annual, and seasonal income declarations into projection-ready amounts.</summary>
public interface IIncomeScheduleService
{
    /// <summary>Returns gross income received in one calendar month after configured annual growth.</summary>
    decimal GrossAmountForMonth(IncomeStream income, DateOnly date, decimal additionalAnnualGrowth = 0m);
    /// <summary>Returns the gross total for a representative year starting at the supplied date.</summary>
    decimal GrossAnnualAmount(IncomeStream income, DateOnly date, decimal additionalAnnualGrowth = 0m);
}

/// <summary>Pure local calculation service for income timing and annual growth.</summary>
public sealed class IncomeScheduleService : IIncomeScheduleService
{
    /// <inheritdoc />
    public decimal GrossAmountForMonth(IncomeStream income, DateOnly date, decimal additionalAnnualGrowth = 0m)
    {
        var annualGrowth = income.AnnualGrowthRate + additionalAnnualGrowth;
        var elapsedYears = Math.Max(0, date.Year - income.StartsOn.Year - (date < income.StartsOn.AddYears(date.Year - income.StartsOn.Year) ? 1 : 0));
        var growthFactor = Pow(1m + annualGrowth, elapsedYears);

        return income.AmountMode switch
        {
            IncomeAmountMode.Monthly => income.MonthlyAmount * growthFactor,
            IncomeAmountMode.Seasonal => income.AnnualAmount * SeasonalShare(income, date.Month) * growthFactor,
            _ => income.AnnualAmount / 12m * growthFactor
        };
    }

    /// <inheritdoc />
    public decimal GrossAnnualAmount(IncomeStream income, DateOnly date, decimal additionalAnnualGrowth = 0m)
    {
        var annualGrowth = income.AnnualGrowthRate + additionalAnnualGrowth;
        var elapsedYears = Math.Max(0, date.Year - income.StartsOn.Year - (date < income.StartsOn.AddYears(date.Year - income.StartsOn.Year) ? 1 : 0));
        var baseAnnualAmount = income.AmountMode == IncomeAmountMode.Monthly ? income.MonthlyAmount * 12m : income.AnnualAmount;
        return baseAnnualAmount * Pow(1m + annualGrowth, elapsedYears);
    }

    /// <summary>Normalises entered month shares so an imperfect total never changes the declared annual income.</summary>
    private static decimal SeasonalShare(IncomeStream income, int month)
    {
        var valid = income.MonthlyAllocations.Where(allocation => allocation.Month is >= 1 and <= 12 && allocation.Share > 0m).ToArray();
        var total = valid.Sum(allocation => allocation.Share);
        if (total <= 0m) return 1m / 12m;
        return valid.Where(allocation => allocation.Month == month).Sum(allocation => allocation.Share) / total;
    }

    /// <summary>Raises a decimal base to a small whole-number exponent without binary floating-point drift.</summary>
    private static decimal Pow(decimal value, int exponent)
    {
        var result = 1m;
        for (var index = 0; index < exponent; index++) result *= value;
        return result;
    }
}
