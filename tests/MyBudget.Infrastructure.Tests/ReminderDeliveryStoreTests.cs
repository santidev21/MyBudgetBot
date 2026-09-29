using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Domain.Users;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The per-day reminder marker across the claim boundary.
/// <para>
/// The unique key <c>(user_id, kind, local_date)</c> is what makes a scheduler that ticks every
/// minute safe: the second claim for the same user, kind and day loses, and a different day or a
/// different kind still wins.
/// </para>
/// </summary>
public sealed class ReminderDeliveryStoreTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly DateOnly Day = new(2026, 9, 15);
    private static readonly DateTimeOffset ClaimedAt =
        new(2026, 9, 15, 21, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task The_first_claim_wins_and_the_second_for_the_same_day_loses()
    {
        await using var context = CreateContext();
        var user = new User(999);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new ReminderDeliveryStore(context);

        (await store.TryClaimAsync(user.Id, "daily", Day, ClaimedAt)).Should().BeTrue();
        (await store.TryClaimAsync(user.Id, "daily", Day, ClaimedAt)).Should().BeFalse();
    }

    [Fact]
    public async Task A_different_day_or_kind_is_a_new_claim()
    {
        await using var context = CreateContext();
        var user = new User(999);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new ReminderDeliveryStore(context);

        (await store.TryClaimAsync(user.Id, "daily", Day, ClaimedAt)).Should().BeTrue();
        (await store.TryClaimAsync(user.Id, "daily", Day.AddDays(1), ClaimedAt)).Should().BeTrue();
        (await store.TryClaimAsync(user.Id, "daily-closing", Day, ClaimedAt)).Should().BeTrue();
    }

    [Fact]
    public async Task A_user_without_an_explicit_value_gets_the_reminder_enabled_from_the_database_default()
    {
        var userId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            // Inserted with raw SQL so the column is omitted and the database default decides,
            // which is exactly what an existing user upgraded by the migration gets.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO users (id, telegram_user_id, currency, time_zone, language, created_at, updated_at)
                 VALUES ({userId}, {999L}, {"COP"}, {"America/Bogota"}, {"es"}, now(), now())
                 """);
        }

        await using (var verification = CreateContext())
        {
            var user = await verification.Users.SingleAsync(candidate => candidate.Id == userId);

            user.DailyReminderEnabled.Should().BeTrue();
        }
    }
}
