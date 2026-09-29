using FluentAssertions;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Dates;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Users;
using NSubstitute;

namespace MyBudget.Application.Tests.Reporting;

/// <summary>
/// The scheduled closing at the application boundary.
/// <para>
/// The rules under test are the moment and the exactly-once claim: the last local day at 23:59
/// closes that month, the first local day is the fallback when that minute was missed, and a
/// month already claimed is never prepared again. The presentation only renders what this
/// returns.
/// </para>
/// </summary>
public sealed class MonthlyClosingServiceTests
{
    private static readonly MonthPeriod August = new(2026, 8);
    private static readonly MonthPeriod September = new(2026, 9);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IReportService _reports = Substitute.For<IReportService>();
    private readonly IMonthlyClosingStore _closings = Substitute.For<IMonthlyClosingStore>();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 1, 5, 0, 0, TimeSpan.Zero));

    private MonthlyClosingService CreateService() =>
        new(_users, _reports, _closings, new UserLocalDate(_clock), _clock);

    [Fact]
    public async Task The_previous_month_is_closed_on_the_users_first_local_day()
    {
        // 05:00 UTC on 1 September is 00:00 on 1 September in Bogotá, so the closing is August's.
        var user = new User(999);
        var month = new MonthlySummary(August, [new BudgetLine(Guid.NewGuid(), "Mercado", "🛒", 1_000_000, 1_500_000)]);
        var statistics = Statistics(August, total: 1_500_000, count: 12, previousTotal: 1_200_000);
        Arrange(user, month, statistics);

        var due = await CreateService().PrepareDueAsync();

        var closing = due.Should().ContainSingle().Subject;
        closing.User.Should().Be(user);
        closing.ClosedPeriod.Should().Be(August);
        closing.NewPeriod.Should().Be(September);
        closing.TotalSpent.Should().Be(1_500_000);
        closing.ExpenseCount.Should().Be(12);
        closing.PreviousTotal.Should().Be(1_200_000);
        closing.TotalAllocated.Should().Be(1_000_000);
        closing.Overspent.Should().Be(500_000);
        closing.IsOverBudget.Should().BeTrue();
        await _closings.Received(1).TryClaimAsync(
            user.Id, August, _clock.GetUtcNow(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_user_whose_local_day_is_not_the_first_is_skipped_even_when_utc_says_first()
    {
        // 02:00 UTC on 1 September is still 21:00 on 31 August in Bogotá, and the last-day
        // closing only fires from 23:59: nothing is due.
        var user = new User(999);
        _clock.Now = new DateTimeOffset(2026, 9, 1, 2, 0, 0, TimeSpan.Zero);
        _users.ListAllAsync(Arg.Any<CancellationToken>()).Returns([user]);

        var due = await CreateService().PrepareDueAsync();

        due.Should().BeEmpty();
        await _closings.DidNotReceiveWithAnyArgs().TryClaimAsync(
            default, default, default, Arg.Any<CancellationToken>());
        await _reports.DidNotReceiveWithAnyArgs().GetMonthlySummaryAsync(
            default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_closing_fires_on_the_last_local_day_at_2359()
    {
        // 23:59 on 30 September in Bogotá is 04:59 UTC on 1 October.
        var user = new User(999);
        _clock.Now = new DateTimeOffset(2026, 10, 1, 4, 59, 0, TimeSpan.Zero);
        _users.ListAllAsync(Arg.Any<CancellationToken>()).Returns([user]);
        _reports.GetMonthlySummaryAsync(user.Id, September, Arg.Any<CancellationToken>())
            .Returns(new MonthlySummary(September, [new BudgetLine(Guid.NewGuid(), "Mercado", "🛒", 1_000_000, 400_000)]));
        _reports.GetStatisticsAsync(user.Id, September, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Statistics(September, 400_000, 3, 0));
        _closings.TryClaimAsync(user.Id, September, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var closing = (await CreateService().PrepareDueAsync()).Should().ContainSingle().Subject;

        closing.ClosedPeriod.Should().Be(September);
        closing.NewPeriod.Should().Be(new MonthPeriod(2026, 10));
        await _closings.Received(1).TryClaimAsync(
            user.Id, September, _clock.GetUtcNow(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_last_day_before_2359_is_not_due_yet()
    {
        // 23:58 on 30 September in Bogotá: one minute early.
        var user = new User(999);
        _clock.Now = new DateTimeOffset(2026, 10, 1, 4, 58, 0, TimeSpan.Zero);
        _users.ListAllAsync(Arg.Any<CancellationToken>()).Returns([user]);

        var due = await CreateService().PrepareDueAsync();

        due.Should().BeEmpty();
        await _reports.DidNotReceiveWithAnyArgs().GetMonthlySummaryAsync(
            default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_month_already_claimed_is_not_prepared_again()
    {
        var user = new User(999);
        Arrange(user, new MonthlySummary(August, []), Statistics(August, 0, 0, 0));
        _closings.TryClaimAsync(user.Id, August, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var due = await CreateService().PrepareDueAsync();

        due.Should().BeEmpty();
    }

    [Fact]
    public async Task The_ranking_keeps_the_biggest_funded_categories_only()
    {
        var user = new User(999);
        var categories = Enumerable.Range(1, 7)
            .Select(index => new CategoryShare(
                Guid.NewGuid(), $"Categoría {index}", "🏷️", index * 100_000, index, index * 5m))
            .Append(new CategoryShare(Guid.NewGuid(), "Sin gasto", "🚫", 0, 0, 0m))
            .ToList();
        var statistics = new PeriodStatistics(
            August, 2_800_000, 28, 31, 90_000, categories, [], [], Comparison(August, 2_800_000, 2_000_000));
        Arrange(user, new MonthlySummary(August, []), statistics);

        var due = await CreateService().PrepareDueAsync();

        var top = due.Should().ContainSingle().Subject.TopCategories;
        top.Should().HaveCount(MonthlyClosingService.TopCategoryCount);
        top.Select(category => category.Name).Should().NotContain("Sin gasto");
        top[0].Spent.Should().Be(700_000);
    }

    [Fact]
    public async Task An_unbudgeted_month_is_never_reported_as_overspent()
    {
        var user = new User(999);
        Arrange(user, new MonthlySummary(August, []), Statistics(August, 400_000, 3, 0));

        var closing = (await CreateService().PrepareDueAsync()).Should().ContainSingle().Subject;

        closing.TotalAllocated.Should().Be(0);
        closing.Overspent.Should().Be(0);
        closing.IsOverBudget.Should().BeFalse();
    }

    [Fact]
    public async Task A_month_with_no_spending_and_no_budget_is_not_sent_or_claimed()
    {
        var user = new User(999);
        Arrange(user, new MonthlySummary(August, []), Statistics(August, 0, 0, 0));

        var due = await CreateService().PrepareDueAsync();

        due.Should().BeEmpty();
        await _closings.DidNotReceiveWithAnyArgs().TryClaimAsync(
            default, default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_budgeted_month_is_still_sent_when_nothing_was_spent()
    {
        var user = new User(999);
        var month = new MonthlySummary(August, [new BudgetLine(Guid.NewGuid(), "Mercado", "🛒", 500_000, 0)]);
        Arrange(user, month, Statistics(August, 0, 0, 0));

        var closing = (await CreateService().PrepareDueAsync()).Should().ContainSingle().Subject;

        closing.TotalAllocated.Should().Be(500_000);
        closing.TotalSpent.Should().Be(0);
    }

    private void Arrange(User user, MonthlySummary summary, PeriodStatistics statistics)
    {
        _users.ListAllAsync(Arg.Any<CancellationToken>()).Returns([user]);
        _reports.GetMonthlySummaryAsync(user.Id, August, Arg.Any<CancellationToken>()).Returns(summary);
        _reports.GetStatisticsAsync(user.Id, August, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(statistics);
        _closings.TryClaimAsync(user.Id, August, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private static PeriodStatistics Statistics(MonthPeriod period, long total, int count, long previousTotal) =>
        new(
            period,
            total,
            count,
            period.LastDay.Day,
            0,
            [],
            [],
            [],
            new PeriodComparison(period, period.Previous, total, previousTotal, false, false));

    private static PeriodComparison Comparison(MonthPeriod period, long total, long previousTotal) =>
        new(period, period.Previous, total, previousTotal, false, false);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
