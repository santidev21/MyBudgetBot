using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using MyBudget.Application.Dates;
using MyBudget.Application.Reminders;
using MyBudget.Domain.Expenses;
using MyBudget.Domain.Users;
using MyBudget.Infrastructure.Persistence;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The daily reminder end to end across the claim boundary.
/// <para>
/// The application service is the real one and the marker lives in PostgreSQL, so this is what
/// proves the delivery is exactly-once per user and local day: a second pass the same evening
/// finds the claim already taken and prepares nothing.
/// </para>
/// </summary>
public sealed class DailyReminderDeliveryTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    // 02:00 UTC is 21:00 in Bogotá on 14 September.
    private static readonly DateTimeOffset Evening =
        new(2026, 9, 15, 2, 0, 0, TimeSpan.Zero);

    private static DailyReminderService BuildService(MyBudgetDbContext context, FakeTimeProvider clock) =>
        new(
            new UserRepository(context),
            new ExpenseReadRepository(context),
            new ReminderDeliveryStore(context),
            new UserLocalDate(clock),
            clock);

    [Fact]
    public async Task An_enabled_user_is_reminded_once_and_the_second_pass_finds_the_claim_taken()
    {
        await using var context = CreateContext();
        var user = new User(999);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var clock = new FakeTimeProvider(Evening);
        var service = BuildService(context, clock);

        var first = await service.PrepareDueAsync();
        var second = await service.PrepareDueAsync();

        first.Should().ContainSingle().Which.LocalDate.Should().Be(new DateOnly(2026, 9, 14));
        second.Should().BeEmpty();

        await using var verification = CreateContext();
        (await verification.ReminderDeliveries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_user_who_already_logged_today_is_not_reminded()
    {
        await using var context = CreateContext();
        var user = new User(999);
        var category = TestData.NewCategory(user.Id, "Mercado");
        context.Users.Add(user);
        context.Categories.Add(category);
        context.Expenses.Add(new Expense(
            user.Id, category.Id, 35_000, "Verduras", new DateOnly(2026, 9, 14), TestData.Today));
        await context.SaveChangesAsync();

        var service = BuildService(context, new FakeTimeProvider(Evening));

        (await service.PrepareDueAsync()).Should().BeEmpty();
    }
}
