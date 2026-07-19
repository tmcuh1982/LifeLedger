using LifeLedger.Api.Domain;

namespace LifeLedger.Api.Services;

/// <summary>Calculates a recurring expense amount from dated user-defined steps and inflation.</summary>
public interface IExpenseScheduleService
{
    /// <summary>Returns the amount per occurrence that applies in a projected month.</summary>
    decimal AmountForOccurrence(Expense expense, DateOnly date, decimal annualInflationRate);
}

/// <summary>Applies each explicit amount step first, then inflation from that step's effective date.</summary>
public sealed class ExpenseScheduleService : IExpenseScheduleService
{
    /// <inheritdoc />
    public decimal AmountForOccurrence(Expense expense, DateOnly date, decimal annualInflationRate)
    {
        var activeChange = expense.AmountChanges
            .Where(change => change.EffectiveOn <= date)
            .MaxBy(change => change.EffectiveOn);
        var baseAmount = activeChange?.Amount ?? expense.MonthlyAmount;
        var anchorDate = activeChange?.EffectiveOn ?? expense.StartsOn;
        if (!expense.IndexedToInflation) return baseAmount;

        // A step is the user's nominal estimate on that date; inflation restarts there to avoid double counting.
        var elapsedMonths = Math.Max(0, (date.Year - anchorDate.Year) * 12 + date.Month - anchorDate.Month);
        return baseAmount * DecimalPow(1m + annualInflationRate, elapsedMonths / 12m);
    }

    /// <summary>Raises a decimal value to a fractional power through a bounded double conversion.</summary>
    private static decimal DecimalPow(decimal value, decimal exponent) => (decimal)Math.Pow((double)value, (double)exponent);
}
