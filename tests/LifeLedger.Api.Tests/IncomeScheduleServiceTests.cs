using LifeLedger.Api.Domain;
using LifeLedger.Api.Services;
using Xunit;

namespace LifeLedger.Api.Tests;

/// <summary>Verifies monthly and seasonal income timing without involving persistence or currency conversion.</summary>
public sealed class IncomeScheduleServiceTests
{
    /// <summary>Keeps the declared annual total even when seasonal percentages do not total one hundred.</summary>
    [Fact]
    public void Seasonal_percentages_are_normalised_without_changing_the_annual_total()
    {
        var service = new IncomeScheduleService();
        var income = new IncomeStream
        {
            StartsOn = new DateOnly(2026, 1, 1),
            AmountMode = IncomeAmountMode.Seasonal,
            AnnualAmount = 12_000m,
            MonthlyAllocations =
            [
                new IncomeMonthlyAllocation { Month = 7, Share = 70m },
                new IncomeMonthlyAllocation { Month = 8, Share = 30m }
            ]
        };

        Assert.Equal(8_400m, service.GrossAmountForMonth(income, new DateOnly(2026, 7, 1)));
        Assert.Equal(3_600m, service.GrossAmountForMonth(income, new DateOnly(2026, 8, 1)));
        Assert.Equal(0m, service.GrossAmountForMonth(income, new DateOnly(2026, 1, 1)));
        Assert.Equal(12_000m, service.GrossAnnualAmount(income, new DateOnly(2026, 1, 1)));
    }

    /// <summary>Spreads an annual total evenly when the user does not enable seasonality.</summary>
    [Fact]
    public void Annual_income_without_seasonality_is_spread_evenly()
    {
        var income = new IncomeStream { StartsOn = new DateOnly(2026, 1, 1), AmountMode = IncomeAmountMode.Annual, AnnualAmount = 18_000m };

        Assert.Equal(1_500m, new IncomeScheduleService().GrossAmountForMonth(income, new DateOnly(2026, 4, 1)));
    }
}
