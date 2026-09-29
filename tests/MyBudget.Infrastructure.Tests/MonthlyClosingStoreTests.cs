using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Domain.Budgets;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The monthly closing markers. They exist only to make the delivery exactly-once, so the tests
/// pin the user and month scope, the single claim and the ownership foreign key.
/// </summary>
public sealed class MonthlyClosingStoreTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly MonthPeriod August = new(2026, 8);
    private static readonly MonthPeriod September = new(2026, 9);

    [Fact]
    public async Task The_first_claim_wins_and_a_second_one_is_refused()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new MonthlyClosingStore(context);

        (await store.TryClaimAsync(user.Id, August, DateTimeOffset.UtcNow)).Should().BeTrue();
        (await store.TryClaimAsync(user.Id, August, DateTimeOffset.UtcNow)).Should().BeFalse();

        await using var verification = CreateContext();
        (await verification.MonthlyClosings.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Each_month_is_claimed_independently()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new MonthlyClosingStore(context);

        (await store.TryClaimAsync(user.Id, August, DateTimeOffset.UtcNow)).Should().BeTrue();
        (await store.TryClaimAsync(user.Id, September, DateTimeOffset.UtcNow)).Should().BeTrue();
    }

    [Fact]
    public async Task One_users_claim_does_not_block_another()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var stranger = TestData.NewUser(2);
        context.Users.AddRange(owner, stranger);
        await context.SaveChangesAsync();

        var store = new MonthlyClosingStore(context);

        (await store.TryClaimAsync(owner.Id, August, DateTimeOffset.UtcNow)).Should().BeTrue();
        (await store.TryClaimAsync(stranger.Id, August, DateTimeOffset.UtcNow)).Should().BeTrue();
    }

    [Fact]
    public async Task A_closing_cannot_belong_to_a_user_that_does_not_exist()
    {
        await using var context = CreateContext();

        var error = await ExpectDatabaseErrorAsync(async () =>
        {
            await context.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO monthly_closings (id, user_id, year, month, sent_at)
                VALUES (gen_random_uuid(), {Guid.NewGuid()}, 2026, 8, now())
                """);
        });

        error.SqlState.Should().Be(ForeignKeyViolation);
        error.ConstraintName.Should().Be("FK_monthly_closings_users_user_id");
    }
}
