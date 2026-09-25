using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Domain.Budgets;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The reason the budget model has its own per-month allocation table: a report for a past
/// month must never depend on the current budget of a category.
/// </summary>
public sealed class HistoricalBudgetTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly MonthPeriod September = new(2026, 9);
    private static readonly MonthPeriod October = new(2026, 10);

    [Fact]
    public async Task Editing_a_later_month_never_rewrites_an_earlier_month()
    {
        var userId = Guid.NewGuid();
        Guid categoryId;

        await using (var context = CreateContext())
        {
            var user = TestData.NewUser();
            var category = TestData.NewCategory(user.Id, "Mercado");
            userId = user.Id;
            categoryId = category.Id;
            context.Users.Add(user);
            context.Categories.Add(category);

            var september = TestData.NewBudget(user.Id, September.Year, September.Month);
            september.SetAllocation(category.Id, 1_000_000);
            context.MonthlyBudgets.Add(september);

            var october = TestData.NewBudget(user.Id, October.Year, October.Month);
            october.SetAllocation(category.Id, 1_500_000);
            context.MonthlyBudgets.Add(october);

            await context.SaveChangesAsync();
        }

        // Read back through a brand new context: nothing is cached from the write above.
        await using (var verification = CreateContext())
        {
            var repository = new BudgetRepository(verification);

            var september = await repository.FindByPeriodAsync(userId, September);
            var october = await repository.FindByPeriodAsync(userId, October);

            september!.TotalAllocated.Should().Be(1_000_000);
            october!.TotalAllocated.Should().Be(1_500_000);
            september.Allocations.Should().ContainSingle(a => a.CategoryId == categoryId);
        }
    }

    [Fact]
    public async Task A_later_month_does_not_inherit_the_previous_month_automatically()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);

        var september = TestData.NewBudget(user.Id, September.Year, September.Month);
        september.SetAllocation(category.Id, 1_000_000);
        context.MonthlyBudgets.Add(september);
        await context.SaveChangesAsync();

        var repository = new BudgetRepository(context);

        (await repository.FindByPeriodAsync(user.Id, October)).Should().BeNull();
    }

    [Fact]
    public async Task Copying_the_previous_month_creates_independent_allocations()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);

        var september = TestData.NewBudget(user.Id, September.Year, September.Month);
        september.SetAllocation(category.Id, 1_000_000);
        context.MonthlyBudgets.Add(september);
        await context.SaveChangesAsync();

        var october = TestData.NewBudget(user.Id, October.Year, October.Month);
        october.CopyAllocationsFrom(september);
        october.SetAllocation(category.Id, 1_500_000);
        context.MonthlyBudgets.Add(october);
        await context.SaveChangesAsync();

        await using var verification = CreateContext();
        var repository = new BudgetRepository(verification);

        (await repository.FindByPeriodAsync(user.Id, September))!.TotalAllocated.Should().Be(1_000_000);
        (await repository.FindByPeriodAsync(user.Id, October))!.TotalAllocated.Should().Be(1_500_000);
    }

    [Fact]
    public async Task An_expense_can_be_recorded_in_a_month_without_a_budget()
    {
        // The budget is a tracking tool, not an authorization system: recording must work
        // even when the user never configured that month.
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        context.Expenses.Add(
            TestData.NewExpense(user.Id, category.Id, 50_000, "Cena", new DateOnly(2026, 10, 3)));
        await context.SaveChangesAsync();

        var budgets = new BudgetRepository(context);
        var expenses = new ExpenseRepository(context);

        (await budgets.FindByPeriodAsync(user.Id, October)).Should().BeNull();

        var octoberExpenses = await expenses.ListByPeriodAsync(user.Id, October);
        octoberExpenses.Should().ContainSingle().Which.Amount.Should().Be(50_000);
    }

    [Fact]
    public async Task Deactivating_a_category_keeps_its_historical_allocations_and_expenses()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);

        var september = TestData.NewBudget(user.Id, September.Year, September.Month);
        september.SetAllocation(category.Id, 1_000_000);
        context.MonthlyBudgets.Add(september);
        context.Expenses.Add(TestData.NewExpense(user.Id, category.Id));
        await context.SaveChangesAsync();

        category.Deactivate();
        await context.SaveChangesAsync();

        var fresh = CreateContext();
        await using (fresh)
        {
            var repository = new CategoryRepository(fresh);

            (await repository.ListAsync(user.Id, includeInactive: false)).Should().BeEmpty();
            (await repository.ListAsync(user.Id, includeInactive: true)).Should().ContainSingle();

            var budgets = new BudgetRepository(fresh);
            (await budgets.FindByPeriodAsync(user.Id, September))!.TotalAllocated.Should().Be(1_000_000);

            var expenses = new ExpenseRepository(fresh);
            (await expenses.ListByPeriodAsync(user.Id, September)).Should().ContainSingle();
        }
    }

    [Fact]
    public async Task Timestamps_are_assigned_as_utc_instants()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);

        var before = DateTime.UtcNow.AddSeconds(-1);
        await context.SaveChangesAsync();

        user.CreatedAt.Should().BeAfter(before);
        user.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        user.UpdatedAt.Should().Be(user.CreatedAt);
    }

    [Fact]
    public async Task Updating_an_expense_advances_UpdatedAt_but_not_CreatedAt()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var expense = TestData.NewExpense(user.Id, category.Id);
        context.Expenses.Add(expense);
        await context.SaveChangesAsync();

        var createdAt = expense.CreatedAt;
        var updatedAt = expense.UpdatedAt;

        await Task.Delay(10);
        expense.ChangeAmount(40_000);
        await context.SaveChangesAsync();

        expense.CreatedAt.Should().Be(createdAt);
        expense.UpdatedAt.Should().BeAfter(updatedAt);
    }
}
