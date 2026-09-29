using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Domain.Budgets;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The budget-alert markers: they exist only to stop the bot repeating itself, so the tests
/// pin the month scope, the uniqueness and the ownership foreign key.
/// </summary>
public sealed class BudgetAlertStoreTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly MonthPeriod September = new(2026, 9);

    [Fact]
    public async Task A_recorded_alert_is_listed_for_its_month_only()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var store = new BudgetAlertStore(context);
        await store.RecordAsync(user.Id, category.Id, September, 80, DateTimeOffset.UtcNow);

        (await store.ListAsync(user.Id, September))
            .Should().ContainSingle()
            .Which.Should().Be(new NotifiedBudgetAlert(category.Id, 80));

        (await store.ListAsync(user.Id, new MonthPeriod(2026, 10))).Should().BeEmpty();
    }

    [Fact]
    public async Task Recording_the_same_threshold_twice_keeps_a_single_row()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var store = new BudgetAlertStore(context);
        await store.RecordAsync(user.Id, category.Id, September, 80, DateTimeOffset.UtcNow);
        await store.RecordAsync(user.Id, category.Id, September, 80, DateTimeOffset.UtcNow);

        await using var verification = CreateContext();
        (await verification.BudgetAlerts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task One_users_alerts_are_never_listed_for_another()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var stranger = TestData.NewUser(2);
        var ownerCategory = TestData.NewCategory(owner.Id);
        var strangerCategory = TestData.NewCategory(stranger.Id, "Transporte");
        context.Users.AddRange(owner, stranger);
        context.Categories.AddRange(ownerCategory, strangerCategory);
        await context.SaveChangesAsync();

        var store = new BudgetAlertStore(context);
        await store.RecordAsync(owner.Id, ownerCategory.Id, September, 100, DateTimeOffset.UtcNow);
        await store.RecordAsync(stranger.Id, strangerCategory.Id, September, 80, DateTimeOffset.UtcNow);

        (await store.ListAsync(owner.Id, September))
            .Should().ContainSingle()
            .Which.Threshold.Should().Be(100);
    }

    [Fact]
    public async Task An_alert_cannot_reference_a_category_owned_by_another_user()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var intruder = TestData.NewUser(2);
        var ownersCategory = TestData.NewCategory(owner.Id);
        context.Users.AddRange(owner, intruder);
        context.Categories.Add(ownersCategory);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO budget_alerts (id, user_id, category_id, year, month, threshold, notified_at)
                VALUES (gen_random_uuid(), {intruder.Id}, {ownersCategory.Id}, 2026, 9, 80, now())
                """);
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("fk_budget_alerts_category_same_user");
    }

    [Fact]
    public async Task The_database_refuses_a_threshold_outside_the_supported_set()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        var category = TestData.NewCategory(user.Id);
        context.Users.Add(user);
        context.Categories.Add(category);
        await context.SaveChangesAsync();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO budget_alerts (id, user_id, category_id, year, month, threshold, notified_at)
                VALUES (gen_random_uuid(), {user.Id}, {category.Id}, 2026, 9, 42, now())
                """);
        });

        error.SqlState.Should().Be(CheckViolation);
        error.ConstraintName.Should().Be("ck_budget_alerts_threshold");
    }
}
