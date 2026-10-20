using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MyBudget.Domain.Users;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// User preferences whose default matters to a screen the user already knows.
/// <para>
/// Existing rows get the database default when the column is added, so both the default and the
/// round trip of an explicit choice are verified against PostgreSQL.
/// </para>
/// </summary>
public sealed class UserDefaultsTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task An_existing_user_shows_spent_in_the_summary_after_the_migration()
    {
        var userId = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            // Inserted with raw SQL so the column is omitted and the database default decides,
            // which is exactly what a user upgraded by the migration gets.
            await context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                 INSERT INTO users (id, telegram_user_id, currency, time_zone, language, created_at, updated_at)
                 VALUES ({userId}, {999L}, {"COP"}, {"America/Bogota"}, {"es"}, now(), now())
                 """);
        }

        await using (var verification = CreateContext())
        {
            var user = await verification.Users.SingleAsync(candidate => candidate.Id == userId);

            user.SummaryShowsRemaining.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Choosing_remaining_in_the_summary_is_persisted()
    {
        var user = new User(999);

        await using (var context = CreateContext())
        {
            context.Users.Add(user);
            await context.SaveChangesAsync();

            user.ChangeSummaryShowsRemaining(true);
            await context.SaveChangesAsync();
        }

        await using (var verification = CreateContext())
        {
            var reloaded = await verification.Users.SingleAsync(candidate => candidate.Id == user.Id);

            reloaded.SummaryShowsRemaining.Should().BeTrue();
        }
    }
}
