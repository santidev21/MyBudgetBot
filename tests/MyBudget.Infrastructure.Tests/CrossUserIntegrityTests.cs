using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Infrastructure.Persistence;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The core multi-user safety net. These are composite foreign keys created with raw SQL in
/// the InitialSchema migration: the owner is part of every reference, so one user's row can
/// never point at another user's category or budget, even if application code is wrong.
/// </summary>
public sealed class CrossUserIntegrityTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task An_expense_cannot_reference_a_category_owned_by_another_user()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var intruder = TestData.NewUser(2);
        var ownersCategory = TestData.NewCategory(owner.Id, "Mercado");
        context.Users.AddRange(owner, intruder);
        context.Categories.Add(ownersCategory);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            context.Expenses.Add(TestData.NewExpense(intruder.Id, ownersCategory.Id));
            await context.SaveChangesAsync();
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("fk_expenses_category_same_user");
    }

    [Fact]
    public async Task A_monthly_allocation_cannot_reference_a_category_owned_by_another_user()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var intruder = TestData.NewUser(2);
        var ownersCategory = TestData.NewCategory(owner.Id, "Mercado");
        var intrudersBudget = TestData.NewBudget(intruder.Id);
        context.Users.AddRange(owner, intruder);
        context.Categories.Add(ownersCategory);
        context.MonthlyBudgets.Add(intrudersBudget);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            // Inserted with raw SQL because that is the only way to express the invalid
            // state: the domain always derives user_id from the category's owner. It also
            // produces the precise, named foreign key error instead of EF's generic
            // concurrency classification.
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO monthly_budget_categories (id, user_id, monthly_budget_id, category_id, amount, created_at, updated_at)
                VALUES (gen_random_uuid(), {intruder.Id}, {intrudersBudget.Id}, {ownersCategory.Id}, 500000, now(), now())
                """);
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("fk_monthly_budget_categories_category_same_user");
    }

    [Fact]
    public async Task A_monthly_allocation_cannot_point_at_another_users_budget()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var intruder = TestData.NewUser(2);
        var ownersBudget = TestData.NewBudget(owner.Id);
        var intrudersCategory = TestData.NewCategory(intruder.Id, "Transporte");
        context.Users.AddRange(owner, intruder);
        context.MonthlyBudgets.Add(ownersBudget);
        context.Categories.Add(intrudersCategory);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO monthly_budget_categories (id, user_id, monthly_budget_id, category_id, amount, created_at, updated_at)
                VALUES (gen_random_uuid(), {intruder.Id}, {ownersBudget.Id}, {intrudersCategory.Id}, 100000, now(), now())
                """);
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("fk_monthly_budget_categories_budget_same_user");
    }

    [Fact]
    public async Task An_alias_cannot_reference_a_category_owned_by_another_user()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var intruder = TestData.NewUser(2);
        var ownersCategory = TestData.NewCategory(owner.Id, "Mercado");
        context.Users.AddRange(owner, intruder);
        context.Categories.Add(ownersCategory);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO category_aliases (id, user_id, category_id, alias, normalized_alias, created_at, updated_at)
                VALUES (gen_random_uuid(), {intruder.Id}, {ownersCategory.Id}, 'robado', 'robado', now(), now())
                """);
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("fk_category_aliases_category_same_user");
    }
}
