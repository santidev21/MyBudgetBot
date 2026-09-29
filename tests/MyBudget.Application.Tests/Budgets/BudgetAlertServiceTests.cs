using FluentAssertions;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Budgets;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using NSubstitute;

namespace MyBudget.Application.Tests.Budgets;

/// <summary>
/// The budget-alert policy in isolation: which thresholds fire, which are remembered and which
/// lines have no threshold at all. The store is substituted so the logic is what is under test.
/// </summary>
public sealed class BudgetAlertServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CategoryId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly MonthPeriod September = new(2026, 9);

    private static (BudgetAlertService Service, IBudgetAlertStore Store) Build(
        MonthlySummary summary, params NotifiedBudgetAlert[] already)
    {
        var reports = Substitute.For<IReportService>();
        reports
            .GetMonthlySummaryAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns(summary);

        var store = Substitute.For<IBudgetAlertStore>();
        store
            .ListAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<NotifiedBudgetAlert>>(already));

        return (new BudgetAlertService(reports, store, TimeProvider.System), store);
    }

    private static MonthlySummary WithLine(long budget, long spent) =>
        new(September, [new BudgetLine(CategoryId, "Mercado", "🛒", budget, spent)]);

    [Fact]
    public async Task Crossing_eighty_percent_reports_the_category_and_records_the_threshold()
    {
        var (service, store) = Build(WithLine(budget: 100_000, spent: 85_000));

        var alerts = await service.EvaluateAsync(UserId, September);

        var alert = alerts.Should().ContainSingle().Subject;
        alert.CategoryId.Should().Be(CategoryId);
        alert.CategoryName.Should().Be("Mercado");
        alert.Icon.Should().Be("🛒");
        alert.Allocated.Should().Be(100_000);
        alert.Spent.Should().Be(85_000);
        alert.Threshold.Should().Be(80);
        alert.UsagePercentage.Should().Be(85m);

        await store.Received(1).RecordAsync(
            UserId, CategoryId, September, 80, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_threshold_that_was_already_announced_is_not_repeated()
    {
        var (service, store) = Build(
            WithLine(budget: 100_000, spent: 85_000),
            new NotifiedBudgetAlert(CategoryId, 80));

        (await service.EvaluateAsync(UserId, September)).Should().BeEmpty();
        await store.DidNotReceive().RecordAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<MonthPeriod>(), Arg.Any<int>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_single_expense_that_jumps_past_the_limit_reports_only_the_highest()
    {
        var (service, store) = Build(WithLine(budget: 100_000, spent: 120_000));

        var alert = (await service.EvaluateAsync(UserId, September)).Should().ContainSingle().Subject;

        alert.Threshold.Should().Be(100);

        // Both thresholds are recorded so neither fires again, but the user sees one message.
        await store.Received(1).RecordAsync(
            UserId, CategoryId, September, 80, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await store.Received(1).RecordAsync(
            UserId, CategoryId, September, 100, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_limit_alert_fires_after_the_near_limit_was_already_sent()
    {
        var (service, _) = Build(
            WithLine(budget: 100_000, spent: 120_000),
            new NotifiedBudgetAlert(CategoryId, 80));

        (await service.EvaluateAsync(UserId, September))
            .Should().ContainSingle()
            .Which.Threshold.Should().Be(100);
    }

    [Fact]
    public async Task A_category_without_allocation_has_no_threshold_to_cross()
    {
        var (service, store) = Build(WithLine(budget: 0, spent: 500_000));

        (await service.EvaluateAsync(UserId, September)).Should().BeEmpty();
        await store.DidNotReceive().RecordAsync(
            Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<MonthPeriod>(), Arg.Any<int>(),
            Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Usage_below_the_warning_line_is_ignored()
    {
        var (service, _) = Build(WithLine(budget: 100_000, spent: 79_900));

        (await service.EvaluateAsync(UserId, September)).Should().BeEmpty();
    }
}
