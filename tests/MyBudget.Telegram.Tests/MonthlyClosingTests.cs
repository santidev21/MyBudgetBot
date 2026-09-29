using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MyBudget.Application.Configuration;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Users;
using MyBudget.Telegram.Presentation;
using MyBudget.Telegram.Reporting;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The monthly closing message. The rendered text is asserted verbatim: the totals, the
/// comparison, the ranked text bars, the overspending note and the copy button are what the user
/// receives, and a change to any of them is a change to the report.
/// </summary>
public sealed class MonthlyClosingTests
{
    private static IUserMessages Messages =>
        new ResourceUserMessages(Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()));

    private static MonthlyClosing Closing(
        long totalSpent = 1_500_000,
        int expenseCount = 12,
        long previousTotal = 1_200_000,
        decimal? change = 25.0m,
        long allocated = 1_000_000,
        IReadOnlyList<ClosingCategory>? top = null) =>
        new(
            new User(999),
            new MonthPeriod(2026, 8),
            new MonthPeriod(2026, 9),
            totalSpent,
            expenseCount,
            previousTotal,
            change,
            allocated,
            top ??
            [
                new ClosingCategory("🍔", "Comida", 700_000, 46.7m),
                new ClosingCategory("🎬", "Ocio", 200_000, 13.3m),
                new ClosingCategory("🚌", "Transporte", 100_000, 6.7m),
            ]);

    private static BotResponse Format(MonthlyClosing closing) =>
        MonthlyClosingMessages.Format(Messages, new MoneyFormatter(new CurrencyRegistry()), closing);

    [Fact]
    public void The_closing_renders_totals_comparison_bars_and_the_overspending_note()
    {
        var message = Format(Closing());

        message.Text.Should().Be(
            """
            📅 Cierre de agosto 2026

            Total gastado: $1.500.000
            Gastos registrados: 12
            El período ya está completo; estas cifras no cambian.

            Comparación con julio 2026:
            ▲ 25 % más que julio 2026.

            Categorías con más gasto:
            🍔 Comida: $700.000 ▓▓▓▓▓▓▓▓▓▓ 46,7 %
            🎬 Ocio: $200.000 ▓▓▓░░░░░░░ 13,3 %
            🚌 Transporte: $100.000 ▓░░░░░░░░░ 6,7 %

            🚨 Te sobregiraste: gastaste $1.500.000 de $1.000.000 asignados.
            """);

        message.Keyboard.Should().BeNull("the closing is text only now that budgets recur");
    }

    [Fact]
    public void A_month_without_a_previous_reference_says_there_is_nothing_to_compare()
    {
        var message = Format(Closing(previousTotal: 0, change: null, allocated: 0, top: []));

        message.Text.Should().Contain("No hubo gastos en julio 2026 para comparar.");
        message.Text.Should().NotContain("Te sobregiraste");
        message.Text.Should().NotContain("Categorías con más gasto");
    }

    [Fact]
    public void Spending_the_same_as_the_previous_month_is_reported_as_flat()
    {
        var message = Format(Closing(totalSpent: 1_200_000, previousTotal: 1_200_000, change: 0m));

        message.Text.Should().Contain("Sin cambios frente a julio 2026.");
    }

    [Fact]
    public async Task Every_due_user_receives_their_own_closing()
    {
        var sender = new RecordingTelegramSender();
        var notifier = new MonthlyClosingNotifier(
            sender, Messages, new MoneyFormatter(new CurrencyRegistry()),
            NullLogger<MonthlyClosingNotifier>.Instance);

        await notifier.NotifyAsync([Closing()]);

        sender.Messages.Should().ContainSingle();
        sender.Messages[0].ChatId.Should().Be(999);
        sender.Messages[0].Text.Should().StartWith("📅 Cierre de agosto 2026");
    }

    [Fact]
    public async Task A_delivery_failure_never_propagates()
    {
        // The marker is already claimed, so a failed send is logged and the pass continues.
        var sender = Substitute.For<ITelegramSender>();
        sender.SendAsync(
                Arg.Any<long>(),
                Arg.Any<IReadOnlyList<BotResponse>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Telegram is down")));

        var notifier = new MonthlyClosingNotifier(
            sender, Messages, new MoneyFormatter(new CurrencyRegistry()),
            NullLogger<MonthlyClosingNotifier>.Instance);

        var act = async () => await notifier.NotifyAsync([Closing()]);

        await act.Should().NotThrowAsync();
    }
}
