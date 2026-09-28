using FluentAssertions;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Budgets;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using NSubstitute;

namespace MyBudget.Application.Tests.Budgets;

/// <summary>
/// The monthly budget use cases against substituted repositories.
/// <para>
/// The rule under test is the product one: the current and future months are editable, a past
/// month is refused with a reason, and copying the previous month only ever happens when the
/// user asks for it.
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

    public BudgetServiceTests() => _service = new BudgetService(_budgets, _categories, _unitOfWork);

    [Fact]
    public async Task Setting_an_allocation_creates_the_month_and_saves()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);

        var result = await _service.SetAllocationAsync(UserId, September, category.Id, 500_000, Today);

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

        await _service.SetAllocationAsync(UserId, September, category.Id, 450_000, Today);

        budget.Allocations.Should().ContainSingle();
        budget.Allocations[0].Amount.Should().Be(450_000);
        _budgets.DidNotReceive().Add(Arg.Any<MonthlyBudget>());
    }

    [Fact]
    public async Task Writing_to_a_past_month_is_refused_with_a_reason()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var result = await _service.SetAllocationAsync(UserId, August, category.Id, 100_000, Today);

        result.Status.Should().Be(BudgetWriteStatus.PastMonth);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Writing_to_a_deactivated_category_is_refused()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        category.Deactivate();
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var result = await _service.SetAllocationAsync(UserId, September, category.Id, 100_000, Today);

        result.Status.Should().Be(BudgetWriteStatus.CategoryInactive);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Writing_for_a_category_that_is_not_the_users_is_refused()
    {
        _categories.FindByIdAsync(UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((BudgetCategory?)null);

        var result = await _service.SetAllocationAsync(UserId, September, Guid.NewGuid(), 100_000, Today);

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
    public async Task Copying_the_previous_month_copies_every_allocation()
    {
        var market = new BudgetCategory(UserId, "Mercado");
        var transport = new BudgetCategory(UserId, "Transporte");
        var previous = new MonthlyBudget(UserId, August);
        previous.SetAllocation(market.Id, 500_000);
        previous.SetAllocation(transport.Id, 120_000);
        _budgets.FindByPeriodAsync(UserId, August, Arg.Any<CancellationToken>()).Returns(previous);
        _budgets.FindByPeriodAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);

        var result = await _service.CopyPreviousMonthAsync(UserId, September, Today);

        result.Status.Should().Be(BudgetCopyStatus.Copied);
        result.Budget!.Allocations.Should().HaveCount(2);
        result.Budget.TotalAllocated.Should().Be(620_000);
        _budgets.Received(1).Add(result.Budget);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Copying_without_a_previous_budget_reports_that_there_is_nothing_to_copy()
    {
        _budgets.FindByPeriodAsync(UserId, August, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);

        var result = await _service.CopyPreviousMonthAsync(UserId, September, Today);

        result.Status.Should().Be(BudgetCopyStatus.NoPreviousBudget);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Copying_an_empty_previous_month_reports_that_there_is_nothing_to_copy()
    {
        var previous = new MonthlyBudget(UserId, August);
        _budgets.FindByPeriodAsync(UserId, August, Arg.Any<CancellationToken>()).Returns(previous);

        var result = await _service.CopyPreviousMonthAsync(UserId, September, Today);

        result.Status.Should().Be(BudgetCopyStatus.NoPreviousBudget);
    }

    [Fact]
    public async Task Copying_into_a_past_month_is_refused()
    {
        var result = await _service.CopyPreviousMonthAsync(UserId, August, Today);

        result.Status.Should().Be(BudgetCopyStatus.PastMonth);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_future_month_is_editable()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);
        _budgets.FindByPeriodAsync(UserId, October, Arg.Any<CancellationToken>())
            .Returns((MonthlyBudget?)null);

        var result = await _service.SetAllocationAsync(UserId, October, category.Id, 700_000, Today);

        result.Saved.Should().BeTrue();
    }
}
