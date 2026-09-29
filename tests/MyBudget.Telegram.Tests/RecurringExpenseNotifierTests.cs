using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MyBudget.Application.Configuration;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Recurring;
using MyBudget.Domain.Users;
using MyBudget.Telegram.Recurring;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The message the recurring pass sends. The expenses are committed before this runs, so the
/// notification must be short, correct and incapable of failing the pass.
/// </summary>
public sealed class RecurringExpenseNotifierTests
{
    private static RecurringExpenseNotifier Build(MyBudget.Telegram.Presentation.ITelegramSender sender) =>
        new(
            sender,
            new ResourceUserMessages(Microsoft.Extensions.Options.Options.Create(new LocalizationOptions())),
            new MoneyFormatter(new CurrencyRegistry()),
            NullLogger<RecurringExpenseNotifier>.Instance);

    private static RecurringApplicationResult Result(User user, params GeneratedRecurringExpense[] generated) =>
        new(user, generated);

    [Fact]
    public async Task Each_generated_expense_is_listed_with_its_date_category_and_amount()
    {
        var sender = new RecordingTelegramSender();
        var user = new User(999, "tester", "Test User");
        var generated = new GeneratedRecurringExpense(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Arriendo", "🏠",
            900_000, "Arriendo", new DateOnly(2026, 9, 1));

        await Build(sender).NotifyAsync([Result(user, generated)]);

        sender.Messages.Should().ContainSingle();
        sender.Messages[0].ChatId.Should().Be(999);
        sender.Messages[0].Text.Should()
            .Contain("Registré")
            .And.Contain("1 de septiembre de 2026")
            .And.Contain("🏠 Arriendo")
            .And.Contain("900.000");
    }

    [Fact]
    public async Task A_delivery_failure_never_propagates()
    {
        // The expense is already recorded; losing the notification must not lose the pass.
        var sender = Substitute.For<MyBudget.Telegram.Presentation.ITelegramSender>();
        sender.SendAsync(
                Arg.Any<long>(),
                Arg.Any<IReadOnlyList<MyBudget.Telegram.Presentation.BotResponse>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Telegram is down")));

        var user = new User(999, "tester", "Test User");
        var generated = new GeneratedRecurringExpense(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "Arriendo", "🏠",
            900_000, null, new DateOnly(2026, 9, 1));
        var notifier = Build(sender);

        var act = async () => await notifier.NotifyAsync([Result(user, generated)]);

        await act.Should().NotThrowAsync();
    }
}
