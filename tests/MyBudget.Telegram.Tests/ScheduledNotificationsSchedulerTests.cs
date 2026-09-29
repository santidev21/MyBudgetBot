using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MyBudget.Application.Configuration;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reminders;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Users;
using MyBudget.Telegram.Reminders;
using MyBudget.Telegram.Reporting;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The scheduled-notifications pass, called directly instead of waiting a real minute.
/// <para>
/// It resolves the scoped reminder and closing services from its own scope and hands whatever
/// comes back to their notifiers. The exactly-once claims and the moments live in the
/// application services; here the wiring is what is pinned.
/// </para>
/// </summary>
public sealed class ScheduledNotificationsSchedulerTests
{
    private static readonly IUserMessages Messages =
        new ResourceUserMessages(Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()));

    private static DailyReminder Reminder() => new(new User(999), new DateOnly(2026, 9, 14));

    private static MonthlyClosing Closing() =>
        new(
            new User(999),
            new MonthPeriod(2026, 8),
            new MonthPeriod(2026, 9),
            1_500_000,
            12,
            1_200_000,
            25m,
            1_000_000,
            [new ClosingCategory("🍔", "Comida", 700_000, 46.7m)]);

    [Fact]
    public async Task A_pass_sends_the_due_reminder()
    {
        var (scheduler, sender, services) = Build(reminders: [Reminder()]);

        await scheduler.RunOnceAsync(CancellationToken.None);

        sender.Messages.Should().ContainSingle();
        sender.Messages[0].Text.Should().Be(Messages.Get("es", MessageKeys.DailyReminderMessage));
        await services.Reminders.Received(1).PrepareDueAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_pass_sends_the_due_closing()
    {
        var (scheduler, sender, services) = Build(closings: [Closing()]);

        await scheduler.RunOnceAsync(CancellationToken.None);

        sender.Messages.Should().ContainSingle();
        sender.Messages[0].Text.Should().StartWith("📅 Cierre de agosto 2026");
        await services.Closings.Received(1).PrepareDueAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_pass_with_nothing_due_sends_nothing()
    {
        var (scheduler, sender, _) = Build();

        await scheduler.RunOnceAsync(CancellationToken.None);

        sender.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_pass_never_throws_out_of_the_scheduler()
    {
        var scheduler = BuildScheduler(
            ThrowingReminders(), Substitute.For<IMonthlyClosingService>(), new RecordingTelegramSender());

        var act = async () => await scheduler.RunOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static IDailyReminderService ThrowingReminders()
    {
        var service = Substitute.For<IDailyReminderService>();
        service.PrepareDueAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<DailyReminder>>(_ => throw new InvalidOperationException("database down"));
        return service;
    }

    private static (ScheduledNotificationsScheduler Scheduler, RecordingTelegramSender Sender,
        (IDailyReminderService Reminders, IMonthlyClosingService Closings) Services)
        Build(IReadOnlyList<DailyReminder>? reminders = null, IReadOnlyList<MonthlyClosing>? closings = null)
    {
        var remindersService = Substitute.For<IDailyReminderService>();
        remindersService.PrepareDueAsync(Arg.Any<CancellationToken>()).Returns(reminders ?? []);
        var closingsService = Substitute.For<IMonthlyClosingService>();
        closingsService.PrepareDueAsync(Arg.Any<CancellationToken>()).Returns(closings ?? []);
        var sender = new RecordingTelegramSender();

        var scheduler = BuildScheduler(remindersService, closingsService, sender);
        return (scheduler, sender, (remindersService, closingsService));
    }

    private static ScheduledNotificationsScheduler BuildScheduler(
        IDailyReminderService reminders, IMonthlyClosingService closings, RecordingTelegramSender sender)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => reminders);
        services.AddScoped(_ => closings);
        var provider = services.BuildServiceProvider();

        var formatter = new MoneyFormatter(new CurrencyRegistry());
        var reminderNotifier = new DailyReminderNotifier(
            sender, Messages, NullLogger<DailyReminderNotifier>.Instance);
        var closingNotifier = new MonthlyClosingNotifier(
            sender, Messages, formatter, NullLogger<MonthlyClosingNotifier>.Instance);

        return new ScheduledNotificationsScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            reminderNotifier,
            closingNotifier,
            NullLogger<ScheduledNotificationsScheduler>.Instance);
    }
}
