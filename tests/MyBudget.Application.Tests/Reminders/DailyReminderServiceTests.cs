using FluentAssertions;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Dates;
using MyBudget.Application.Reminders;
using MyBudget.Domain.Users;
using NSubstitute;

namespace MyBudget.Application.Tests.Reminders;

/// <summary>
/// The daily reminder decision at the application boundary.
/// <para>
/// The rules under test are the moment (the user's own 21:00), the opt-out, the "already logged
/// today" skip and the exactly-once claim. The presentation only sends what this returns.
/// </para>
/// </summary>
public sealed class DailyReminderServiceTests
{
    private static readonly DateOnly LocalDay = new(2026, 9, 14);

    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IExpenseReadRepository _expenses = Substitute.For<IExpenseReadRepository>();
    private readonly IReminderDeliveryStore _deliveries = Substitute.For<IReminderDeliveryStore>();
    private readonly FixedClock _clock = new(new DateTimeOffset(2026, 9, 15, 2, 0, 0, TimeSpan.Zero));

    private DailyReminderService CreateService() =>
        new(_users, _expenses, _deliveries, new UserLocalDate(_clock), _clock);

    [Fact]
    public async Task A_user_past_21_local_without_an_expense_is_reminded_once()
    {
        // 02:00 UTC is 21:00 in Bogotá on 14 September.
        var user = new User(999);
        Arrange(user);

        var due = await CreateService().PrepareDueAsync();

        var reminder = due.Should().ContainSingle().Subject;
        reminder.User.Should().Be(user);
        reminder.LocalDate.Should().Be(LocalDay);
        await _deliveries.Received(1).TryClaimAsync(
            user.Id, DailyReminderService.Kind, LocalDay, _clock.GetUtcNow(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Before_the_reminder_hour_nothing_is_due()
    {
        var user = new User(999);
        // 01:00 UTC is 20:00 in Bogotá: still too early.
        _clock.Now = new DateTimeOffset(2026, 9, 15, 1, 0, 0, TimeSpan.Zero);
        Arrange(user);

        var due = await CreateService().PrepareDueAsync();

        due.Should().BeEmpty();
        await _deliveries.DidNotReceiveWithAnyArgs().TryClaimAsync(
            default, null!, default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_user_who_turned_the_reminder_off_is_skipped()
    {
        var user = new User(999);
        user.ChangeDailyReminder(false);
        Arrange(user);

        var due = await CreateService().PrepareDueAsync();

        due.Should().BeEmpty();
        await _expenses.DidNotReceiveWithAnyArgs().ExistsOnAsync(default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_user_who_already_logged_an_expense_today_is_not_reminded()
    {
        var user = new User(999);
        Arrange(user);
        _expenses.ExistsOnAsync(user.Id, LocalDay, Arg.Any<CancellationToken>()).Returns(true);

        var due = await CreateService().PrepareDueAsync();

        due.Should().BeEmpty();
        await _deliveries.DidNotReceiveWithAnyArgs().TryClaimAsync(
            default, null!, default, default, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_day_already_claimed_is_not_reminded_again()
    {
        var user = new User(999);
        Arrange(user);
        _deliveries.TryClaimAsync(user.Id, DailyReminderService.Kind, LocalDay, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var due = await CreateService().PrepareDueAsync();

        due.Should().BeEmpty();
    }

    private void Arrange(User user)
    {
        _users.ListAllAsync(Arg.Any<CancellationToken>()).Returns([user]);
        _expenses.ExistsOnAsync(user.Id, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(false);
        _deliveries.TryClaimAsync(
                user.Id, DailyReminderService.Kind, Arg.Any<DateOnly>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
