using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MyBudget.Application.Configuration;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Users;
using MyBudget.Telegram.Reporting;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The scheduler pass, called directly instead of waiting for a real day.
/// <para>
/// It resolves the scoped closing service from its own scope and hands whatever comes back to
/// the notifier. The exactly-once claim lives in the application service; here the wiring is
/// what is pinned.
/// </para>
/// </summary>
public sealed class MonthlyClosingSchedulerTests
{
    private static readonly IUserMessages Messages =
        new ResourceUserMessages(Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()));

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
    public async Task A_pass_sends_every_due_closing()
    {
        var (scheduler, sender, service) = Build(Closing());

        await scheduler.RunOnceAsync(CancellationToken.None);

        sender.Messages.Should().ContainSingle();
        sender.Messages[0].Text.Should().StartWith("📅 Cierre de agosto 2026");
        await service.Received(1).PrepareDueAsync(Arg.Any<CancellationToken>());
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
        var service = Substitute.For<IMonthlyClosingService>();
        service.PrepareDueAsync(Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<MonthlyClosing>>(_ => throw new InvalidOperationException("database down"));
        var scheduler = BuildScheduler(service, new RecordingTelegramSender());

        var act = async () => await scheduler.RunOnceAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    private static (MonthlyClosingScheduler Scheduler, RecordingTelegramSender Sender, IMonthlyClosingService Service)
        Build(params MonthlyClosing[] due)
    {
        var service = Substitute.For<IMonthlyClosingService>();
        service.PrepareDueAsync(Arg.Any<CancellationToken>()).Returns(due);
        var sender = new RecordingTelegramSender();

        return (BuildScheduler(service, sender), sender, service);
    }

    private static MonthlyClosingScheduler BuildScheduler(
        IMonthlyClosingService service, RecordingTelegramSender sender)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => service);
        var provider = services.BuildServiceProvider();

        var notifier = new MonthlyClosingNotifier(
            sender,
            Messages,
            new MoneyFormatter(new CurrencyRegistry()),
            NullLogger<MonthlyClosingNotifier>.Instance);

        return new MonthlyClosingScheduler(
            provider.GetRequiredService<IServiceScopeFactory>(),
            notifier,
            NullLogger<MonthlyClosingScheduler>.Instance);
    }
}
