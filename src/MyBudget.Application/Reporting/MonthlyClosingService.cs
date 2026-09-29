using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Dates;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Users;

namespace MyBudget.Application.Reporting;

/// <summary>One category's spending inside a closing report, ranked by how much it took.</summary>
public sealed record ClosingCategory(string Icon, string Name, long Spent, decimal SharePercentage);

/// <summary>
/// The closing of a month: what was spent, how it compares with the month before and where the
/// money went, ready for the presentation to render.
/// <para>
/// <see cref="ClosedPeriod"/> is always a whole past month, so its comparison is final. When the
/// month had a budget and the spending passed it, <see cref="Overspent"/> carries the excess so
/// the renderer can say so instead of hiding it.
/// </para>
/// </summary>
public sealed record MonthlyClosing(
    User User,
    MonthPeriod ClosedPeriod,
    MonthPeriod NewPeriod,
    long TotalSpent,
    int ExpenseCount,
    long PreviousTotal,
    decimal? ChangePercentage,
    long TotalAllocated,
    IReadOnlyList<ClosingCategory> TopCategories)
{
    public long Overspent =>
        TotalAllocated > 0 && TotalSpent > TotalAllocated ? TotalSpent - TotalAllocated : 0;

    public bool IsOverBudget => Overspent > 0;
}

/// <summary>
/// Builds the monthly closing and decides, exactly once, which users are due to receive it.
/// <para>
/// The trigger is the user's own calendar day: on the first day of a month, the closing of the
/// previous one is prepared. The claim is written before a closing is returned, so a restart
/// or a second pass the same day cannot send it twice. All the numbers come from
/// <see cref="IReportService"/>; this service only decides the moment and the shape.
/// </para>
/// </summary>
public interface IMonthlyClosingService
{
    /// <summary>
    /// The closings due right now: one per user whose local date is the first of the month, who
    /// had something to report in the closed month (spending or a budget) and whose closing was
    /// not sent yet. Claimed closings are the only ones returned.
    /// </summary>
    Task<IReadOnlyList<MonthlyClosing>> PrepareDueAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class MonthlyClosingService(
    IUserRepository users,
    IReportService reports,
    IMonthlyClosingStore closings,
    IUserLocalDate localDate,
    TimeProvider timeProvider) : IMonthlyClosingService
{
    /// <summary>How many of the closed month's biggest categories the closing shows.</summary>
    public const int TopCategoryCount = 5;

    /// <summary>
    /// The last-day closing fires at this local time. The first-day pass stays as the fallback,
    /// so a bot that was down at 23:59 still sends it.
    /// </summary>
    public static readonly TimeSpan ClosingTime = new(23, 59, 0);

    public async Task<IReadOnlyList<MonthlyClosing>> PrepareDueAsync(
        CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var all = await users.ListAllAsync(cancellationToken);
        var due = new List<MonthlyClosing>();

        foreach (var user in all)
        {
            // The user's own wall clock, never UTC: the last hour of the month in Bogotá may be
            // the first of the next one in UTC, and the other way around.
            var local = localDate.LocalNow(user.TimeZone);
            var today = DateOnly.FromDateTime(local.DateTime);

            if (!TryResolvePeriod(local, today, out var closedPeriod, out var newPeriod))
            {
                continue;
            }

            var summary = await reports.GetMonthlySummaryAsync(user.Id, closedPeriod, cancellationToken);
            var statistics = await reports.GetStatisticsAsync(
                user.Id, closedPeriod, today, cancellationToken);

            // A month with no spending and no allocation has nothing to report, so the closing
            // is deliberately not sent and the marker is not spent. Talking about nothing is how
            // a notification feature gets muted; the app is always there for the numbers.
            if (statistics.Total == 0 && summary.TotalAllocated == 0)
            {
                continue;
            }

            // Claim before returning anything: the first pass for this user and month wins, and
            // a restart can only get false from here on.
            if (!await closings.TryClaimAsync(user.Id, closedPeriod, now, cancellationToken))
            {
                continue;
            }

            var top = statistics.Categories
                .Where(category => category.Spent > 0)
                .OrderByDescending(category => category.Spent)
                .ThenBy(category => category.CategoryName, StringComparer.Ordinal)
                .Take(TopCategoryCount)
                .Select(category => new ClosingCategory(
                    category.Icon, category.CategoryName, category.Spent, category.SharePercentage))
                .ToList();

            due.Add(new MonthlyClosing(
                user,
                closedPeriod,
                newPeriod,
                statistics.Total,
                statistics.ExpenseCount,
                statistics.Comparison.PreviousTotal,
                statistics.Comparison.ChangePercentage,
                summary.TotalAllocated,
                top));
        }

        return due;
    }

    /// <summary>
    /// Decides whether a closing is due for this user right now, and which month it covers.
    /// <para>
    /// The last day of the month at 23:59 local closes that month; the first day of the next
    /// month is the fallback when the bot missed that minute. Both resolve to the same closed
    /// month, so the marker keeps the delivery exactly-once across the two paths.
    /// </para>
    /// </summary>
    private static bool TryResolvePeriod(
        DateTimeOffset local, DateOnly today, out MonthPeriod closedPeriod, out MonthPeriod newPeriod)
    {
        if (today.Day == 1)
        {
            newPeriod = MonthPeriod.FromDate(today);
            closedPeriod = newPeriod.Previous;
            return true;
        }

        var isLastDay = today.Day == DateTime.DaysInMonth(today.Year, today.Month);
        if (isLastDay && local.TimeOfDay >= ClosingTime)
        {
            closedPeriod = MonthPeriod.FromDate(today);
            newPeriod = closedPeriod.Next;
            return true;
        }

        closedPeriod = default;
        newPeriod = default;
        return false;
    }
}
