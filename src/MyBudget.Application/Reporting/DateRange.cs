using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Reporting;

/// <summary>
/// An inclusive calendar date range, always resolved in the user's local time zone.
/// <para>
/// Reports are periods of local calendar dates, never UTC instants: a range that mixes the two
/// would move expenses into the wrong month at the boundary. This type only describes the
/// range; resolving <c>today</c> is <c>IUserLocalDate</c>'s job.
/// </para>
/// </summary>
public readonly record struct DateRange
{
    public DateRange(DateOnly from, DateOnly to)
    {
        if (to < from)
        {
            throw new ArgumentException(
                "The end of a range cannot precede its start.", nameof(to));
        }

        From = from;
        To = to;
    }

    public DateOnly From { get; }

    public DateOnly To { get; }

    /// <summary>The whole of a budget month, its first through its last calendar day.</summary>
    public static DateRange ForMonth(MonthPeriod period) => new(period.FirstDay, period.LastDay);

    public bool Contains(DateOnly date) => date >= From && date <= To;

    /// <summary>Number of calendar days covered, both ends included.</summary>
    public int Days => To.DayNumber - From.DayNumber + 1;

    /// <summary>ISO representation used in logs: <c>2026-09-01..2026-09-30</c>.</summary>
    public override string ToString() => $"{From:yyyy-MM-dd}..{To:yyyy-MM-dd}";
}
