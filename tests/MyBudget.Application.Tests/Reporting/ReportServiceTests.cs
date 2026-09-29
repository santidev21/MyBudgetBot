using FluentAssertions;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Expenses;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;
using NSubstitute;

namespace MyBudget.Application.Tests.Reporting;

/// <summary>
/// The report service against substituted read repositories. The behaviour that matters is the
/// merge of historical allocation with aggregated spending, the keyset paging boundary, and the
/// incomplete-period flag: the SQL itself is proven against PostgreSQL in the infrastructure
/// tests.
/// </summary>
public sealed class ReportServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly MonthPeriod September = new(2026, 9);
    private static readonly DateOnly Today = new(2026, 9, 15);

    private readonly IExpenseReadRepository _expenseQueries = Substitute.For<IExpenseReadRepository>();
    private readonly IBudgetRepository _budgets = Substitute.For<IBudgetRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly ReportService _service;

    public ReportServiceTests() => _service = new ReportService(_expenseQueries, _budgets, _categories);

    [Fact]
    public async Task The_summary_merges_the_historical_allocation_with_the_months_spending()
    {
        var food = new BudgetCategory(UserId, "Comida", "🍔");
        var fun = new BudgetCategory(UserId, "Ocio", "🎬");
        var budget = new MonthlyBudget(UserId, September);
        budget.SetAllocation(food.Id, 1_000_000);

        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>()).Returns(budget);
        _expenseQueries.SumByCategoryAsync(UserId, Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns([new CategoryTotal(food.Id, 700_000, 5), new CategoryTotal(fun.Id, 200_000, 2)]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([food, fun]);

        var summary = await _service.GetMonthlySummaryAsync(UserId, September);

        summary.TotalAllocated.Should().Be(1_000_000);
        summary.TotalSpent.Should().Be(900_000);
        summary.Lines.Should().HaveCount(2);

        var foodLine = summary.Lines.Single(line => line.CategoryId == food.Id);
        foodLine.Budget.Should().Be(1_000_000);
        foodLine.Spent.Should().Be(700_000);
        foodLine.UsagePercentage.Should().Be(70m);

        // Ocio was never allocated to, so usage is unknown rather than a division by zero.
        var funLine = summary.Lines.Single(line => line.CategoryId == fun.Id);
        funLine.Budget.Should().Be(0);
        funLine.HasBudget.Should().BeFalse();
        funLine.UsagePercentage.Should().BeNull();
    }

    [Fact]
    public async Task Overspending_is_reported_not_refused()
    {
        var food = new BudgetCategory(UserId, "Comida", "🍔");
        var budget = new MonthlyBudget(UserId, September);
        budget.SetAllocation(food.Id, 100_000);

        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>()).Returns(budget);
        _expenseQueries.SumByCategoryAsync(UserId, Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns([new CategoryTotal(food.Id, 130_000, 3)]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([food]);

        var summary = await _service.GetMonthlySummaryAsync(UserId, September);
        var line = summary.Lines.Single();

        line.IsOverspent.Should().BeTrue();
        line.Overage.Should().Be(30_000);
        line.UsagePercentage.Should().Be(130m);
    }

    [Fact]
    public async Task A_deactivated_category_without_allocation_or_spending_is_hidden()
    {
        var active = new BudgetCategory(UserId, "Comida", "🍔");
        var retired = new BudgetCategory(UserId, "Viajes", "✈️");
        retired.Deactivate();

        _expenseQueries.SumByCategoryAsync(UserId, Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns([new CategoryTotal(active.Id, 10_000, 1)]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([active, retired]);

        var summary = await _service.GetMonthlySummaryAsync(UserId, September);

        summary.Lines.Should().ContainSingle().Which.CategoryId.Should().Be(active.Id);
    }

    [Fact]
    public async Task The_history_returns_a_cursor_only_when_more_rows_exist()
    {
        var category = new BudgetCategory(UserId, "Comida", "🍔");
        var first = NewExpense(category.Id, 10_000, new DateOnly(2026, 9, 10));
        var second = NewExpense(category.Id, 20_000, new DateOnly(2026, 9, 9));

        _expenseQueries.ListPageAsync(
                UserId, Arg.Any<DateRange>(), null, 3, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([first, second]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([category]);

        var page = await _service.GetHistoryAsync(
            UserId, DateRange.ForMonth(September), after: null, pageSize: 2);

        page.Items.Should().HaveCount(2);
        page.Items[0].CategoryName.Should().Be("Comida");
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task The_history_cursor_points_at_the_last_row_of_a_full_page()
    {
        var category = new BudgetCategory(UserId, "Comida", "🍔");
        var first = NewExpense(category.Id, 10_000, new DateOnly(2026, 9, 10));
        var second = NewExpense(category.Id, 20_000, new DateOnly(2026, 9, 9));
        var overflow = NewExpense(category.Id, 30_000, new DateOnly(2026, 9, 8));

        // Asking for pageSize + 1 is how the service discovers a further page.
        _expenseQueries.ListPageAsync(
                UserId, Arg.Any<DateRange>(), null, 3, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns([first, second, overflow]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([category]);

        var page = await _service.GetHistoryAsync(
            UserId, DateRange.ForMonth(September), after: null, pageSize: 2);

        page.Items.Should().HaveCount(2);
        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().Be(new ExpensePageCursor(second.ExpenseDate, second.Id));
    }

    [Fact]
    public async Task The_statistics_share_the_total_and_average_over_the_days_elapsed()
    {
        var food = new BudgetCategory(UserId, "Comida", "🍔");
        var fun = new BudgetCategory(UserId, "Ocio", "🎬");

        _expenseQueries.SumAsync(UserId, Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<DateRange>().From.Month == 8 ? 100_000 : 150_000);
        _expenseQueries.SumByCategoryAsync(UserId, Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns([new CategoryTotal(food.Id, 90_000, 3), new CategoryTotal(fun.Id, 60_000, 2)]);
        _expenseQueries.SumByDayAsync(UserId, Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns([new DailyTotal(new DateOnly(2026, 9, 10), 150_000, 5)]);
        _expenseQueries.ListLargestAsync(UserId, Arg.Any<DateRange>(), 5, Arg.Any<CancellationToken>())
            .Returns([NewExpense(food.Id, 90_000, new DateOnly(2026, 9, 10))]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([food, fun]);

        var statistics = await _service.GetStatisticsAsync(UserId, September, Today);

        statistics.Total.Should().Be(150_000);
        statistics.ExpenseCount.Should().Be(5);
        statistics.DaysCounted.Should().Be(15);
        statistics.AverageDaily.Should().Be(10_000);

        statistics.Categories.Should().HaveCount(2);
        statistics.Categories[0].CategoryName.Should().Be("Comida");
        statistics.Categories[0].SharePercentage.Should().Be(60m);
        statistics.Categories[1].SharePercentage.Should().Be(40m);

        statistics.Largest.Should().ContainSingle()
            .Which.CategoryName.Should().Be("Comida");

        statistics.Comparison.PreviousTotal.Should().Be(100_000);
        statistics.Comparison.Difference.Should().Be(50_000);
        statistics.Comparison.ChangePercentage.Should().Be(50m);

        // September is still in progress, so the caller has to say so.
        statistics.Comparison.PeriodIncomplete.Should().BeTrue();
        statistics.Comparison.PreviousPeriodIncomplete.Should().BeFalse();
    }

    [Fact]
    public async Task A_finished_month_is_not_flagged_incomplete()
    {
        _expenseQueries.SumAsync(UserId, Arg.Any<DateRange>(), Arg.Any<CancellationToken>()).Returns(0L);
        _expenseQueries.SumByCategoryAsync(UserId, Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CategoryTotal>());
        _expenseQueries.SumByDayAsync(UserId, Arg.Any<DateRange>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<DailyTotal>());
        _expenseQueries.ListLargestAsync(UserId, Arg.Any<DateRange>(), 5, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<Expense>());
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<BudgetCategory>());

        var statistics = await _service.GetStatisticsAsync(UserId, new MonthPeriod(2026, 8), Today);

        statistics.Comparison.PeriodIncomplete.Should().BeFalse();
        statistics.DaysCounted.Should().Be(31);
        statistics.AverageDaily.Should().Be(0);
    }

    private static Expense NewExpense(Guid categoryId, long amount, DateOnly date) =>
        new(UserId, categoryId, amount, "Algo", date, Today);
}
