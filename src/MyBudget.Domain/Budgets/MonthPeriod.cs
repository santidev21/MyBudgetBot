namespace MyBudget.Domain.Budgets;

/// <summary>
/// A calendar month, independent of any time zone arithmetic.
/// A budget month is always resolved from the user's local <see cref="DateOnly"/>,
/// never from a UTC instant.
/// </summary>
public readonly record struct MonthPeriod
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    public MonthPeriod(int year, int month)
    {
        if (year is < MinYear or > MaxYear)
        {
            throw new ArgumentOutOfRangeException(
                nameof(year), year, $"Year must be between {MinYear} and {MaxYear}.");
        }

        if (month is < 1 or > 12)
        {
            throw new ArgumentOutOfRangeException(
                nameof(month), month, "Month must be between 1 and 12.");
        }

        Year = year;
        Month = month;
    }

    public int Year { get; }

    public int Month { get; }

    public static MonthPeriod FromDate(DateOnly date) => new(date.Year, date.Month);

    public MonthPeriod Previous =>
        Month == 1 ? new MonthPeriod(Year - 1, 12) : new MonthPeriod(Year, Month - 1);

    public MonthPeriod Next =>
        Month == 12 ? new MonthPeriod(Year + 1, 1) : new MonthPeriod(Year, Month + 1);

    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => new(Year, Month, DateTime.DaysInMonth(Year, Month));

    public bool Contains(DateOnly date) => date.Year == Year && date.Month == Month;

    /// <summary>ISO-ish representation used in logs and callback data: <c>2026-09</c>.</summary>
    public override string ToString() => $"{Year:D4}-{Month:D2}";
}
