using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Infrastructure.Persistence;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// Verifies the constraints that live in PostgreSQL. The domain already rejects most of
/// these states, so the invalid rows are produced with raw SQL on purpose: the point is
/// that the database refuses them even when application code is bypassed.
/// </summary>
public sealed class ConstraintsTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task Telegram_user_id_must_be_unique()
    {
        await using var context = CreateContext();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            context.Users.Add(TestData.NewUser(777));
            await context.SaveChangesAsync();

            context.Users.Add(TestData.NewUser(777));
            await context.SaveChangesAsync();
        });

        error.SqlState.Should().Be(UniqueViolation);
        error.ConstraintName.Should().Be("uq_users_telegram_user_id");
    }

    [Fact]
    public async Task Category_names_are_unique_per_user_ignoring_case_and_whitespace()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        context.Categories.Add(TestData.NewCategory(user.Id, "Mercado"));
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            context.Categories.Add(TestData.NewCategory(user.Id, "  MERCADO "));
            await context.SaveChangesAsync();
        });

        error.SqlState.Should().Be(UniqueViolation);
        error.ConstraintName.Should().Be("uq_categories_user_name");
    }

    [Fact]
    public async Task Two_users_may_use_the_same_category_name()
    {
        await using var context = CreateContext();
        var first = TestData.NewUser(1);
        var second = TestData.NewUser(2);
        context.Users.AddRange(first, second);
        context.Categories.Add(TestData.NewCategory(first.Id, "Mercado"));
        context.Categories.Add(TestData.NewCategory(second.Id, "Mercado"));

        await context.SaveChangesAsync();

        (await context.Categories.CountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task A_category_name_cannot_be_blank()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO categories (id, user_id, name, icon, is_active, created_at, updated_at)
                VALUES (gen_random_uuid(), {user.Id}, '   ', '📦', true, now(), now())
                """);
        });

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("ck_categories_name");
    }

    [Fact]
    public async Task An_expense_amount_must_be_greater_than_zero()
    {
        await using var context = CreateContext();
        var (userId, categoryId) = await SeedUserAndCategoryAsync(context);

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO expenses (id, user_id, category_id, amount, description, expense_date, categorization_source, created_at, updated_at)
                VALUES (gen_random_uuid(), {userId}, {categoryId}, 0, NULL, DATE '2026-09-15', 'manual', now(), now())
                """);
        });

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("ck_expenses_amount");
    }

    [Fact]
    public async Task An_expense_amount_cannot_exceed_the_upper_bound()
    {
        await using var context = CreateContext();
        var (userId, categoryId) = await SeedUserAndCategoryAsync(context);

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO expenses (id, user_id, category_id, amount, description, expense_date, categorization_source, created_at, updated_at)
                VALUES (gen_random_uuid(), {userId}, {categoryId}, 1000000000000, NULL, DATE '2026-09-15', 'manual', now(), now())
                """);
        });

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("ck_expenses_amount");
    }

    [Fact]
    public async Task A_categorization_source_outside_the_allowed_set_is_rejected()
    {
        await using var context = CreateContext();
        var (userId, categoryId) = await SeedUserAndCategoryAsync(context);

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO expenses (id, user_id, category_id, amount, description, expense_date, categorization_source, created_at, updated_at)
                VALUES (gen_random_uuid(), {userId}, {categoryId}, 35000, NULL, DATE '2026-09-15', 'guessed_by_ai', now(), now())
                """);
        });

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("ck_expenses_source");
    }

    [Fact]
    public async Task A_monthly_budget_month_must_be_between_1_and_12()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO monthly_budgets (id, user_id, year, month, created_at, updated_at)
                VALUES (gen_random_uuid(), {user.Id}, 2026, 13, now(), now())
                """);
        });

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("ck_monthly_budgets_month");
    }

    [Fact]
    public async Task A_monthly_allocation_cannot_be_negative()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        var budget = TestData.NewBudget(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        context.MonthlyBudgets.Add(budget);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO monthly_budget_categories (id, user_id, monthly_budget_id, category_id, amount, created_at, updated_at)
                VALUES (gen_random_uuid(), {user.Id}, {budget.Id}, {category.Id}, -1, now(), now())
                """);
        });

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("ck_monthly_budget_categories_amount");
    }

    [Fact]
    public async Task There_can_only_be_one_budget_per_user_and_month()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        context.MonthlyBudgets.Add(TestData.NewBudget(user.Id, 2026, 9));
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            context.MonthlyBudgets.Add(TestData.NewBudget(user.Id, 2026, 9));
            await context.SaveChangesAsync();
        });

        error.SqlState.Should().Be(UniqueViolation);
        error.ConstraintName.Should().Be("uq_monthly_budgets_period");
    }

    [Fact]
    public async Task A_normalized_alias_cannot_repeat_within_one_category()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        category.AddAlias("Verduras", "verduras");
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO category_aliases (id, user_id, category_id, alias, normalized_alias, created_at, updated_at)
                VALUES (gen_random_uuid(), {user.Id}, {category.Id}, 'Verduras', 'verduras', now(), now())
                """);
        });

        error.SqlState.Should().Be(UniqueViolation);
        error.ConstraintName.Should().Be("uq_category_aliases_category_normalized");
    }

    [Fact]
    public async Task The_same_alias_may_exist_on_two_categories_on_purpose()
    {
        // Deliberate design decision: "comida" can legitimately mean restaurants or
        // groceries. Duplicate aliases are how the matcher detects genuine ambiguity,
        // so the database must not collapse them.
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var restaurants = TestData.NewCategory(user.Id, "Comidas", "🍽️");
        var groceries = TestData.NewCategory(user.Id, "Mercado", "🛒");
        restaurants.AddAlias("comida", "comida");
        groceries.AddAlias("comida", "comida");
        context.Users.Add(user);
        context.Categories.AddRange(restaurants, groceries);

        await context.SaveChangesAsync();

        (await context.CategoryAliases.CountAsync()).Should().Be(2);
    }

    private static async Task<(Guid UserId, Guid CategoryId)> SeedUserAndCategoryAsync(
        MyBudgetDbContext context)
    {
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();
        return (user.Id, category.Id);
    }
}
