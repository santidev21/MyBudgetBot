using FluentAssertions;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Expenses;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;
using NSubstitute;

namespace MyBudget.Application.Tests.Expenses;

/// <summary>
/// The expense use cases against substituted repositories. The interesting behaviour is the
/// guard rails around a write: a category that is not the user's, a non-positive amount and a
/// future date are all reported as reasons, never written.
/// </summary>
public sealed class ExpenseServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Today = new(2026, 9, 28);
    private static readonly MonthPeriod September = new(2026, 9);

    private readonly IExpenseRepository _expenses = Substitute.For<IExpenseRepository>();
    private readonly ICategoryRepository _categories = Substitute.For<ICategoryRepository>();
    private readonly IUnitOfWork _unitOfWork = Substitute.For<IUnitOfWork>();
    private readonly ExpenseService _service;

    public ExpenseServiceTests() => _service = new ExpenseService(_expenses, _categories, _unitOfWork);

    [Fact]
    public async Task Creating_an_expense_persists_it()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var result = await _service.CreateAsync(
            UserId, category.Id, 35_000, "Verduras", new DateOnly(2026, 9, 27), Today);

        result.Saved.Should().BeTrue();
        result.Expense!.Amount.Should().Be(35_000);
        _expenses.Received(1).Add(result.Expense);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creating_with_a_category_that_is_not_the_users_is_refused()
    {
        _categories.FindByIdAsync(UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((BudgetCategory?)null);

        var result = await _service.CreateAsync(
            UserId, Guid.NewGuid(), 35_000, null, Today, Today);

        result.Status.Should().Be(ExpenseChangeStatus.CategoryNotFound);
        _expenses.DidNotReceiveWithAnyArgs().Add(default!);
    }

    [Fact]
    public async Task A_non_positive_amount_is_refused()
    {
        var result = await _service.CreateAsync(UserId, Guid.NewGuid(), 0, null, Today, Today);

        result.Status.Should().Be(ExpenseChangeStatus.InvalidAmount);
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_future_date_is_refused()
    {
        var result = await _service.CreateAsync(
            UserId, Guid.NewGuid(), 35_000, null, new DateOnly(2026, 9, 29), Today);

        result.Status.Should().Be(ExpenseChangeStatus.InvalidDate);
    }

    [Fact]
    public async Task Updating_changes_the_amount_description_and_date()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var expense = new Expense(UserId, category.Id, 35_000, "Verduras", new DateOnly(2026, 9, 20), Today);
        _expenses.FindByIdAsync(UserId, expense.Id, Arg.Any<CancellationToken>()).Returns(expense);

        var result = await _service.UpdateAsync(
            UserId, expense.Id, category.Id, 42_000, "Frutas", new DateOnly(2026, 9, 21), Today);

        result.Saved.Should().BeTrue();
        expense.Amount.Should().Be(42_000);
        expense.Description.Should().Be("Frutas");
        expense.ExpenseDate.Should().Be(new DateOnly(2026, 9, 21));
    }

    [Fact]
    public async Task Changing_the_category_on_update_records_a_manual_choice()
    {
        var original = new BudgetCategory(UserId, "Mercado");
        var chosen = new BudgetCategory(UserId, "Restaurantes");
        var expense = new Expense(
            UserId, original.Id, 35_000, "Almuerzo", new DateOnly(2026, 9, 20), Today,
            CategorizationSource.Matched);
        _expenses.FindByIdAsync(UserId, expense.Id, Arg.Any<CancellationToken>()).Returns(expense);
        _categories.FindByIdAsync(UserId, chosen.Id, Arg.Any<CancellationToken>()).Returns(chosen);

        var result = await _service.UpdateAsync(
            UserId, expense.Id, chosen.Id, 35_000, "Almuerzo", new DateOnly(2026, 9, 20), Today);

        result.Saved.Should().BeTrue();
        expense.CategoryId.Should().Be(chosen.Id);
        expense.CategorizationSource.Should().Be(CategorizationSource.Manual);
    }

    [Fact]
    public async Task Updating_an_expense_that_is_not_the_users_reports_not_found()
    {
        _expenses.FindByIdAsync(UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Expense?)null);

        var result = await _service.UpdateAsync(
            UserId, Guid.NewGuid(), Guid.NewGuid(), 10_000, null, Today, Today);

        result.Status.Should().Be(ExpenseChangeStatus.NotFound);
    }

    [Fact]
    public async Task Deleting_removes_the_expense()
    {
        var category = new BudgetCategory(UserId, "Mercado");
        var expense = new Expense(UserId, category.Id, 35_000, null, Today, Today);
        _expenses.FindByIdAsync(UserId, expense.Id, Arg.Any<CancellationToken>()).Returns(expense);

        var deleted = await _service.DeleteAsync(UserId, expense.Id);

        deleted.Should().BeTrue();
        _expenses.Received(1).Remove(expense);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Deleting_a_missing_expense_reports_false()
    {
        _expenses.FindByIdAsync(UserId, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Expense?)null);

        (await _service.DeleteAsync(UserId, Guid.NewGuid())).Should().BeFalse();
    }

    [Fact]
    public async Task Getting_one_expense_returns_its_category_label()
    {
        var category = new BudgetCategory(UserId, "Mercado", "🛒");
        var expense = new Expense(UserId, category.Id, 35_000, "Verduras", Today, Today);
        _expenses.FindByIdAsync(UserId, expense.Id, Arg.Any<CancellationToken>()).Returns(expense);
        _categories.FindByIdAsync(UserId, category.Id, Arg.Any<CancellationToken>()).Returns(category);

        var item = await _service.GetItemAsync(UserId, expense.Id);

        item.Should().NotBeNull();
        item!.CategoryName.Should().Be("Mercado");
        item.Icon.Should().Be("🛒");
        item.Amount.Should().Be(35_000);
    }

    [Fact]
    public async Task The_month_list_joins_category_labels_and_keeps_deactivated_categories()
    {
        var active = new BudgetCategory(UserId, "Mercado", "🛒");
        var deactivated = new BudgetCategory(UserId, "Viajes", "✈️");
        deactivated.Deactivate();

        var first = new Expense(UserId, active.Id, 35_000, "Verduras", new DateOnly(2026, 9, 27), Today);
        var second = new Expense(UserId, deactivated.Id, 90_000, "Taxi", new DateOnly(2026, 9, 26), Today);
        _expenses.ListByPeriodAsync(UserId, September, Arg.Any<CancellationToken>())
            .Returns([first, second]);
        _categories.ListAsync(UserId, true, Arg.Any<CancellationToken>()).Returns([active, deactivated]);

        var items = await _service.ListMonthAsync(UserId, September);

        items.Should().HaveCount(2);
        items[0].CategoryName.Should().Be("Mercado");
        items[0].Icon.Should().Be("🛒");
        items[1].CategoryName.Should().Be("Viajes");
        items[1].Amount.Should().Be(90_000);
    }
}
