using FluentAssertions;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Budgets;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using NSubstitute;

namespace MyBudget.Application.Tests.Budgets;

/// <summary>
/// The budget use cases against substituted repositories.
/// <para>
/// The rules under test are the product ones: the current and future months are editable, a past
/// month is refused with a reason, an assignment can be a one-month override or the recurring
/// default, and the default never reaches a month it did not cover.
/// </para>
/// </summary>
public sealed class BudgetServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly MonthPeriod September = new(2026, 9);
    private static readonly MonthPeriod August = new(2026, 8);
    private static readonly MonthPeriod October = new(2026, 10);

    private readonly IBudgetRepository _budgets = Substitute.For<IBudgetRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly BudgetService _service;

    public BudgetServiceTests()
    {
        _budgets.ListDefaultsAsync(UserId, Arg.Any<CancellationToken>()).Returns([]);
        _budgets.FindDefaultAsync(UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((BudgetDefault?)null);
        _service = new BudgetService(_budgets, _categories, _unitOfWork);
    }

    [Fact]
    public async Task Setting_an_allocation_creates_the_month_and_saves()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);

        var result = await _service.SetAllocationAsync(
            UserId, September, category.Id, 500_000, BudgetScope.Month, Today);

        result.Saved.Should().BeTrue();
        result.Budget!.Allocations.Should().ContainSingle()
            .Which.Amount.Should().Be(500_000);
        _budgets.Received(1).Add(result.Budget);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Setting_the_same_category_again_updates_the_existing_allocation()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var budget = new MonthlyBudget(UserId, September);
        budget.SetAllocation(category.Id, 300_000);
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>()).Returns(budget);

        await _service.SetAllocationAsync(
            UserId, September, category.Id, 450_000, BudgetScope.Month, Today);

        budget.Allocations.Should().ContainSingle();
        budget.Allocations[0].Amount.Should().Be(450_000);
        _budgets.DidNotReceive().Add(Arg.Any<MonthlyBudget>());
    }

    [Fact]
    public async Task Assigning_to_all_months_creates_a_default_and_clears_the_month_override()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var budget = new MonthlyBudget(UserId, September);
        budget.SetAllocation(category.Id, 300_000);
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>()).Returns(budget);

        var result = await _service.SetAllocationAsync(
            UserId, September, category.Id, 500_000, BudgetScope.AllMonths, Today);

        result.Saved.Should().BeTrue();
        _budgets.Received(1).AddDefault(Arg.Is<BudgetDefault>(created =>
            created.CategoryId == category.Id
            && created.Amount == 500_000
            && created.EffectiveFrom == September));
        budget.Allocations.Should().BeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Editing_the_default_keeps_the_month_it_started()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var existing = new BudgetDefault(UserId, category.Id, September, 300_000);
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _budgets.FindDefaultAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(existing);

        await _service.SetAllocationAsync(
            UserId, October, category.Id, 500_000, BudgetScope.AllMonths, Today);

        existing.Amount.Should().Be(500_000);
        existing.EffectiveFrom.Should().Be(September);
        _budgets.DidNotReceive().AddDefault(Arg.Any<BudgetDefault>());
    }

    [Fact]
    public async Task Writing_to_a_past_month_is_refused_with_a_reason()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var result = await _service.SetAllocationAsync(
            UserId, August, category.Id, 100_000, BudgetScope.Month, Today);

        result.Status.Should().Be(BudgetWriteStatus.PastMonth);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Writing_to_a_deactivated_category_is_refused()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        category.Deactivate();
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var result = await _service.SetAllocationAsync(
            UserId, September, category.Id, 100_000, BudgetScope.Month, Today);

        result.Status.Should().Be(BudgetWriteStatus.CategoryInactive);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Writing_for_a_category_that_is_not_the_users_is_refused()
    {
        _categories.FindByIdAsync(UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((BudgetCategory?)null);

        var result = await _service.SetAllocationAsync(
            UserId, September, Guid.NewGuid(), 100_000, BudgetScope.Month, Today);

        result.Status.Should().Be(BudgetWriteStatus.CategoryNotFound);
    }

    [Fact]
    public async Task Removing_an_allocation_takes_it_out_of_the_month()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var budget = new MonthlyBudget(UserId, September);
        budget.SetAllocation(category.Id, 300_000);
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>()).Returns(budget);

        var result = await _service.RemoveAllocationAsync(UserId, September, category.Id, Today);

        result.Saved.Should().BeTrue();
        budget.Allocations.Should().BeEmpty();
    }

    [Fact]
    public async Task The_month_lists_active_categories_even_when_they_have_no_allocation()
    {
        var active = new BudgetCategory(UserId, "Mercado");
        var deactivated = new BudgetCategory(UserId, "Viajes");
        deactivated.Deactivate();
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([active, deactivated]);

        var view = await _service.GetMonthAsync(UserId, September);

        view.TotalAllocated.Should().Be(0);
        view.Lines.Should().ContainSingle().Which.CategoryId.Should().Be(active.Id);
    }

    [Fact]
    public async Task A_deactivated_category_that_holds_an_allocation_still_renders()
    {
        var active = new BudgetCategory(UserId, "Mercado");
        var deactivated = new BudgetCategory(UserId, "Viajes");
        deactivated.Deactivate();
        var budget = new MonthlyBudget(UserId, September);
        budget.SetAllocation(deactivated.Id, 250_000);
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>()).Returns(budget);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([active, deactivated]);

        var view = await _service.GetMonthAsync(UserId, September);

        view.Lines.Should().HaveCount(2);
        view.Lines.Should().Contain(line => line.CategoryId == deactivated.Id && line.Amount == 250_000);
        view.TotalAllocated.Should().Be(250_000);
    }

    [Fact]
    public async Task A_month_without_its_own_row_reads_the_recurring_default()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);
        _budgets.ListDefaultsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new BudgetDefault(UserId, category.Id, September, 500_000)]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([category]);

        var view = await _service.GetMonthAsync(UserId, September);

        view.TotalAllocated.Should().Be(500_000);
        view.Lines.Should().ContainSingle().Which.IsRecurring.Should().BeTrue();
    }

    [Fact]
    public async Task A_default_does_not_reach_a_month_before_it_started()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);
        _budgets.ListDefaultsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new BudgetDefault(UserId, category.Id, October, 500_000)]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([category]);

        var view = await _service.GetMonthAsync(UserId, September);

        view.TotalAllocated.Should().Be(0);
        view.Lines.Should().ContainSingle().Which.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public async Task A_month_override_wins_over_the_default()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var budget = new MonthlyBudget(UserId, September);
        budget.SetAllocation(category.Id, 300_000);
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>()).Returns(budget);
        _budgets.ListDefaultsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new BudgetDefault(UserId, category.Id, September, 500_000)]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([category]);

        var view = await _service.GetMonthAsync(UserId, September);

        view.TotalAllocated.Should().Be(300_000);
        view.Lines.Should().ContainSingle().Which.IsRecurring.Should().BeFalse();
    }

    [Fact]
    public async Task Having_defaults_is_reported_from_the_repository()
    {
        _budgets.ListDefaultsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns([new BudgetDefault(UserId, Guid.NewGuid(), September, 1)]);

        (await _service.HasDefaultsAsync(UserId)).Should().BeTrue();
    }

    [Fact]
    public async Task A_future_month_is_editable()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _budgets.FindByPeriodAsync(UserId, October, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);

        var result = await _service.SetAllocationAsync(
            UserId, October, category.Id, 700_000, BudgetScope.Month, Today);

        result.Saved.Should().BeTrue();
    }

    [Fact]
    public async Task Promoting_a_month_creates_defaults_from_that_month()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var budget = new MonthlyBudget(UserId, September);
        budget.SetAllocation(category.Id, 300_000);
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>()).Returns(budget);

        var result = await _service.PromoteMonthToDefaultsAsync(UserId, September, Today);

        result.Saved.Should().BeTrue();
        result.PromotedCount.Should().Be(1);
        _budgets.Received(1).AddDefault(Arg.Is<BudgetDefault>(created =>
            created.CategoryId == category.Id
            && created.Amount == 300_000
            && created.EffectiveFrom == September));
        budget.Allocations.Should().BeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Promoting_keeps_an_existing_defaults_effective_from()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var existing = new BudgetDefault(UserId, category.Id, September, 300_000);
        var budget = new MonthlyBudget(UserId, October);
        budget.SetAllocation(category.Id, 500_000);
        _budgets.FindByPeriodAsync(UserId, October, Arg.Any<CancellationToken>()).Returns(budget);
        _budgets.FindDefaultAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(existing);

        var result = await _service.PromoteMonthToDefaultsAsync(UserId, October, Today);

        result.PromotedCount.Should().Be(1);
        existing.Amount.Should().Be(500_000);
        existing.EffectiveFrom.Should().Be(September);
        budget.Allocations.Should().BeEmpty();
        _budgets.DidNotReceive().AddDefault(Arg.Any<BudgetDefault>());
    }

    [Fact]
    public async Task Promoting_leaves_a_default_that_starts_later_untouched()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var future = new BudgetDefault(UserId, category.Id, new MonthPeriod(2026, 11), 300_000);
        var budget = new MonthlyBudget(UserId, October);
        budget.SetAllocation(category.Id, 500_000);
        _budgets.FindByPeriodAsync(UserId, October, Arg.Any<CancellationToken>()).Returns(budget);
        _budgets.FindDefaultAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(future);

        var result = await _service.PromoteMonthToDefaultsAsync(UserId, October, Today);

        result.PromotedCount.Should().Be(0);
        future.Amount.Should().Be(300_000);
        future.EffectiveFrom.Should().Be(new MonthPeriod(2026, 11));

        // The override has to stay, or October would lose the allocation it is showing.
        budget.Allocations.Should().ContainSingle();
    }

    [Fact]
    public async Task Promoting_a_month_without_overrides_promotes_nothing()
    {
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);

        var result = await _service.PromoteMonthToDefaultsAsync(UserId, September, Today);

        result.Saved.Should().BeTrue();
        result.PromotedCount.Should().Be(0);
        _budgets.DidNotReceive().AddDefault(Arg.Any<BudgetDefault>());
    }

    [Fact]
    public async Task Promoting_a_past_month_is_refused_with_a_reason()
    {
        var result = await _service.PromoteMonthToDefaultsAsync(UserId, August, Today);

        result.Status.Should().Be(BudgetWriteStatus.PastMonth);
        result.PromotedCount.Should().Be(0);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
