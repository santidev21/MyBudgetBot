using FluentAssertions;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;
using MyBudget.Domain.Users;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// Every repository method takes the owner id first. These tests prove the scope is actually
/// applied, so one user's data can never be read through another user's session.
/// </summary>
public sealed class UserIsolationTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Category_lookup_does_not_cross_the_user_boundary()
    {
        await using var context = CreateContext();
        var (first, firstCategory) = await SeedUserWithCategoryAsync(context, 1, "Mercado");
        var (second, secondCategory) = await SeedUserWithCategoryAsync(context, 2, "Transporte");

        var repository = new CategoryRepository(context);

        (await repository.FindByIdAsync(first.Id, firstCategory.Id)).Should().NotBeNull();
        (await repository.FindByIdAsync(second.Id, secondCategory.Id)).Should().NotBeNull();
        (await repository.FindByIdAsync(first.Id, secondCategory.Id)).Should().BeNull();
        (await repository.FindByIdAsync(second.Id, firstCategory.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Category_list_only_contains_the_owners_rows()
    {
        await using var context = CreateContext();
        var (first, _) = await SeedUserWithCategoryAsync(context, 1, "Mercado");
        await SeedUserWithCategoryAsync(context, 2, "Transporte");

        var repository = new CategoryRepository(context);
        var categories = await repository.ListAsync(first.Id, includeInactive: true);

        categories.Should().ContainSingle().Which.Name.Should().Be("Mercado");
    }

    [Fact]
    public async Task Category_name_lookup_is_case_insensitive_and_user_scoped()
    {
        await using var context = CreateContext();
        var (first, _) = await SeedUserWithCategoryAsync(context, 1, "Mercado");
        var (second, _) = await SeedUserWithCategoryAsync(context, 2, "Transporte");

        var repository = new CategoryRepository(context);

        (await repository.FindByNameAsync(first.Id, "mercado")).Should().NotBeNull();
        (await repository.FindByNameAsync(first.Id, "transporte")).Should().BeNull();
        (await repository.FindByNameAsync(second.Id, "transporte")).Should().NotBeNull();
    }

    [Fact]
    public async Task Expense_lookup_does_not_cross_the_user_boundary()
    {
        await using var context = CreateContext();
        var (first, firstCategory) = await SeedUserWithCategoryAsync(context, 1, "Mercado");
        var (second, secondCategory) = await SeedUserWithCategoryAsync(context, 2, "Transporte");

        var firstExpense = TestData.NewExpense(first.Id, firstCategory.Id);
        context.Expenses.Add(firstExpense);
        await context.SaveChangesAsync();

        var repository = new ExpenseRepository(context);

        (await repository.FindByIdAsync(first.Id, firstExpense.Id)).Should().NotBeNull();
        (await repository.FindByIdAsync(second.Id, firstExpense.Id)).Should().BeNull();
        repository.FindByIdAsync(first.Id, firstExpense.Id).Should().NotBeNull();
        secondCategory.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task Expense_list_only_contains_the_owners_rows()
    {
        await using var context = CreateContext();
        var (first, firstCategory) = await SeedUserWithCategoryAsync(context, 1, "Mercado");
        var (second, secondCategory) = await SeedUserWithCategoryAsync(context, 2, "Transporte");

        context.Expenses.Add(TestData.NewExpense(first.Id, firstCategory.Id, 35_000, "Verduras"));
        context.Expenses.Add(TestData.NewExpense(second.Id, secondCategory.Id, 60_000, "Gasolina"));
        await context.SaveChangesAsync();

        var repository = new ExpenseRepository(context);
        var period = new MonthPeriod(2026, 9);

        var firstExpenses = await repository.ListByPeriodAsync(first.Id, period);

        firstExpenses.Should().ContainSingle().Which.Description.Should().Be("Verduras");
    }

    [Fact]
    public async Task Budget_lookup_does_not_cross_the_user_boundary()
    {
        await using var context = CreateContext();
        var (first, firstCategory) = await SeedUserWithCategoryAsync(context, 1, "Mercado");
        var (second, secondCategory) = await SeedUserWithCategoryAsync(context, 2, "Transporte");

        var period = new MonthPeriod(2026, 9);
        var firstBudget = TestData.NewBudget(first.Id, period.Year, period.Month);
        firstBudget.SetAllocation(firstCategory.Id, 1_000_000);
        var secondBudget = TestData.NewBudget(second.Id, period.Year, period.Month);
        secondBudget.SetAllocation(secondCategory.Id, 500_000);
        context.MonthlyBudgets.AddRange(firstBudget, secondBudget);
        await context.SaveChangesAsync();

        var repository = new BudgetRepository(context);

        var found = await repository.FindByPeriodAsync(first.Id, period);

        found.Should().NotBeNull();
        found!.TotalAllocated.Should().Be(1_000_000);
        found.Allocations.Should().ContainSingle(a => a.CategoryId == firstCategory.Id);
    }

    private static async Task<(User User, BudgetCategory Category)> SeedUserWithCategoryAsync(
        MyBudgetDbContext context, long telegramUserId, string categoryName)
    {
        var user = TestData.NewUser(telegramUserId);
        var category = TestData.NewCategory(user.Id, categoryName);
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return (user, category);
    }
}
