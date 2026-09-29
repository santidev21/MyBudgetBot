using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Recurring;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// Cascade behaviour and the rule that history can never be destroyed.
/// The composite foreign keys use ON DELETE NO ACTION (not RESTRICT) precisely so that
/// deleting a user can cascade through categories, budgets and expenses in one statement.
/// </summary>
public sealed class CascadeTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Erasing_a_user_removes_every_owned_row()
    {
        Guid userId;

        await using (var context = CreateContext())
        {
            var user = TestData.NewUser();
            var category = TestData.NewCategory(user.Id, "Mercado");
            category.AddAlias("Verduras", "verduras");
            var budget = TestData.NewBudget(user.Id, 2026, 9);
            budget.SetAllocation(category.Id, 1_000_000);
            userId = user.Id;

            context.Users.Add(user);
            context.Categories.Add(category);
            context.MonthlyBudgets.Add(budget);
            context.Expenses.Add(TestData.NewExpense(user.Id, category.Id));
            context.RecurringExpenses.Add(
                new RecurringExpense(user.Id, category.Id, 900_000, "Arriendo", 1, new DateOnly(2026, 9, 1)));

            await context.SaveChangesAsync();
        }

        await using (var erasure = CreateContext())
        {
            await new UserDataEraser(erasure).EraseAsync(userId);
        }

        await using (var verification = CreateContext())
        {
            (await verification.Users.CountAsync()).Should().Be(0);
            (await verification.Categories.CountAsync()).Should().Be(0);
            (await verification.CategoryAliases.CountAsync()).Should().Be(0);
            (await verification.MonthlyBudgets.CountAsync()).Should().Be(0);
            (await verification.MonthlyBudgetCategories.CountAsync()).Should().Be(0);
            (await verification.Expenses.CountAsync()).Should().Be(0);
            (await verification.RecurringExpenses.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task Erasing_a_user_leaves_another_users_data_untouched()
    {
        Guid doomedUserId;

        await using (var context = CreateContext())
        {
            var doomed = TestData.NewUser(1);
            var survivor = TestData.NewUser(2);
            var doomedCategory = TestData.NewCategory(doomed.Id, "Mercado");
            var survivorCategory = TestData.NewCategory(survivor.Id, "Mercado");
            doomedUserId = doomed.Id;

            context.Users.AddRange(doomed, survivor);
            context.Categories.AddRange(doomedCategory, survivorCategory);
            context.Expenses.Add(TestData.NewExpense(doomed.Id, doomedCategory.Id));
            context.Expenses.Add(TestData.NewExpense(survivor.Id, survivorCategory.Id));

            await context.SaveChangesAsync();
        }

        await using (var erasure = CreateContext())
        {
            await new UserDataEraser(erasure).EraseAsync(doomedUserId);
        }

        await using (var verification = CreateContext())
        {
            (await verification.Users.CountAsync()).Should().Be(1);
            (await verification.Categories.CountAsync()).Should().Be(1);
            (await verification.Expenses.CountAsync()).Should().Be(1);
        }
    }

    [Fact]
    public async Task A_bare_user_delete_is_not_the_supported_erasure_path()
    {
        // Erasure goes through IUserDataEraser, which deletes children in dependency order
        // inside one transaction. This documents that the schema does not offer a reliable
        // one-statement user delete across the NO ACTION category constraints, so callers
        // must not rely on one.
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        var budget = TestData.NewBudget(user.Id, 2026, 9);
        budget.SetAllocation(category.Id, 1_000_000);
        context.Users.Add(user);
        context.Categories.Add(category);
        context.MonthlyBudgets.Add(budget);
        context.Expenses.Add(TestData.NewExpense(user.Id, category.Id));
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM users WHERE id = {user.Id}");
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
    }

    [Fact]
    public async Task Deleting_a_category_that_has_expenses_is_blocked()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        context.Expenses.Add(TestData.NewExpense(user.Id, category.Id));
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM categories WHERE id = {category.Id}");
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("fk_expenses_category_same_user");
    }

    [Fact]
    public async Task Deleting_a_category_that_still_funds_a_budget_is_blocked()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        var budget = TestData.NewBudget(user.Id, 2026, 9);
        budget.SetAllocation(category.Id, 1_000_000);
        context.Users.Add(user);
        context.Categories.Add(category);
        context.MonthlyBudgets.Add(budget);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM categories WHERE id = {category.Id}");
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("fk_monthly_budget_categories_category_same_user");
    }

    [Fact]
    public async Task Deleting_a_category_that_still_has_a_recurring_rule_is_blocked()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Arriendo");
        context.Users.Add(user);
        context.Categories.Add(category);
        context.RecurringExpenses.Add(
            new RecurringExpense(user.Id, category.Id, 900_000, "Arriendo", 1, new DateOnly(2026, 9, 1)));
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM categories WHERE id = {category.Id}");
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("fk_recurring_expenses_category_same_user");
    }

    [Fact]
    public async Task An_allocation_is_persisted_for_the_owner()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        var budget = TestData.NewBudget(user.Id, 2026, 9);
        context.Users.Add(user);
        context.Categories.Add(category);
        context.MonthlyBudgets.Add(budget);
        await context.SaveChangesAsync();

        budget.SetAllocation(category.Id, 1_000_000);
        await context.SaveChangesAsync();

        await using var verification = CreateContext();
        var reloaded = await new BudgetRepository(verification)
            .FindByPeriodAsync(user.Id, new MonthPeriod(2026, 9));

        reloaded.Should().NotBeNull();
        reloaded!.Allocations.Should().ContainSingle(a => a.CategoryId == category.Id);
        reloaded.TotalAllocated.Should().Be(1_000_000);
    }

    [Fact]
    public async Task Deleting_a_budget_removes_only_its_allocations()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Mercado");
        var budget = TestData.NewBudget(user.Id, 2026, 9);
        budget.SetAllocation(category.Id, 1_000_000);
        context.Users.Add(user);
        context.Categories.Add(category);
        context.MonthlyBudgets.Add(budget);
        context.Expenses.Add(TestData.NewExpense(user.Id, category.Id));
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM monthly_budgets WHERE id = {budget.Id}");

        await using var verification = CreateContext();
        (await verification.MonthlyBudgetCategories.CountAsync()).Should().Be(0);
        (await verification.Categories.CountAsync()).Should().Be(1);
        (await verification.Expenses.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_an_unreferenced_category_is_allowed()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id, "Nunca usada");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM categories WHERE id = {category.Id}");

        await using var verification = CreateContext();
        (await verification.Categories.CountAsync()).Should().Be(0);
    }
}
