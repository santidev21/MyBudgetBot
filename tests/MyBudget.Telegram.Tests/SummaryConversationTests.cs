using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Localization;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The monthly summary, driven through the real router.
/// <para>
/// The rendered text is asserted verbatim: the totals, the usage, the text bars and the month
/// navigation are what the user sees, and a change to any of them is a change to the report.
/// </para>
/// </summary>
public sealed class SummaryConversationTests
{
    private const long ChatId = 4242;

    private static readonly BudgetLine Food = new(
        Guid.Parse("11111111-1111-1111-1111-111111111111"), "Comida", "🍔", 1_000_000, 700_000);

    private static readonly BudgetLine Fun = new(
        Guid.Parse("22222222-2222-2222-2222-222222222222"), "Ocio", "🎬", 0, 200_000);

    private static ConversationContext ContextFor(
        TelegramHarness harness, MonthPeriod? period = null, string? state = null) =>
        new(
            harness.User,
            ChatId,
            state is null
                ? null
                : new ConversationSnapshot(
                    harness.User.Id,
                    ChatId,
                    SummaryConversation.ConversationName,
                    state,
                    (period is null ? new ReportingPayload() : new ReportingPayload().WithPeriod(period.Value))
                        .Serialize(),
                    TestClock.Now.AddMinutes(30)));

    [Fact]
    public async Task The_summary_renders_totals_bars_and_month_navigation()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetMonthlySummaryAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(new MonthlySummary(new MonthPeriod(2026, 9), [Food, Fun]));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuSummary), CancellationToken.None);

        turn.NextState.Should().Be("summary");
        turn.Responses.Should().ContainSingle();

        var message = turn.Responses[0];
        message.Text.Should().Be(
            """
            📊 Resumen de septiembre 2026

            Total gastado: $900.000
            Presupuesto: $1.000.000 · Uso: 90 %
            Te quedan $100.000 este mes.

            🍔 Comida: $700.000 de $1.000.000 ▓▓▓▓▓▓▓░░░ 70 %
            🎬 Ocio: $200.000 —
            """);

        var buttons = message.Keyboard!.Rows.SelectMany(row => row).ToList();
        buttons.Should().HaveCount(4);
        buttons[0].Text.Should().Be("📂 Por categoría");
        buttons[0].CallbackData.Should().Be(CategoryDetailConversation.OpenCallback);
        buttons[1].Text.Should().Be("← agosto");
        buttons[1].CallbackData.Should().Be("sum:prev");
        buttons[2].Text.Should().Be("septiembre 2026");
        buttons[3].Text.Should().Be("octubre →");
        buttons[3].CallbackData.Should().Be("sum:next");
    }

    [Fact]
    public async Task An_empty_month_says_there_is_nothing_to_show()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetMonthlySummaryAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(new MonthlySummary(new MonthPeriod(2026, 9), []));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuSummary), CancellationToken.None);

        turn.Responses[0].Text.Should().Be(
            """
            📊 Resumen de septiembre 2026

            Total gastado: $0

            No hay gastos ni presupuesto registrados en este mes.
            """);
    }

    [Fact]
    public async Task Overspending_is_shown_rather_than_blocked()
    {
        var harness = TelegramHarness.Build();
        var overspent = new BudgetLine(Guid.NewGuid(), "Comida", "🍔", 100_000, 130_000);
        harness.ReportService
            .GetMonthlySummaryAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(new MonthlySummary(new MonthPeriod(2026, 9), [overspent]));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuSummary), CancellationToken.None);

        turn.Responses[0].Text.Should().Contain("$130.000 de $100.000");
        turn.Responses[0].Text.Should().Contain("▓▓▓▓▓▓▓▓▓▓");
        turn.Responses[0].Text.Should().Contain("130 %");
    }

    [Fact]
    public async Task The_next_button_moves_to_the_following_month()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetMonthlySummaryAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(new MonthlySummary(new MonthPeriod(2026, 10), []));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, new MonthPeriod(2026, 9), "summary"),
            new IncomingCallback("cb", "sum:next"),
            CancellationToken.None);

        turn.Responses[0].Text.Should().StartWith("📊 Resumen de octubre 2026");
        await harness.ReportService.Received(1).GetMonthlySummaryAsync(
            harness.User.Id, new MonthPeriod(2026, 10), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_previous_button_moves_to_the_previous_month()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetMonthlySummaryAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(new MonthlySummary(new MonthPeriod(2026, 8), []));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, new MonthPeriod(2026, 9), "summary"),
            new IncomingCallback("cb", "sum:prev"),
            CancellationToken.None);

        turn.Responses[0].Text.Should().StartWith("📊 Resumen de agosto 2026");
    }

    [Fact]
    public async Task A_compact_expense_typed_while_the_summary_is_on_screen_is_recorded()
    {
        var harness = TelegramHarness.Build();
        var food = new BudgetCategory(harness.User.Id, "Comida", "🍔");
        harness.CategoryService
            .ListAsync(harness.User.Id, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetCategory>>([food]));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, new MonthPeriod(2026, 10), "summary"),
            "115509 carnes colanta",
            CancellationToken.None);

        // The summary does not swallow the message: it becomes the expense flow.
        turn.NextState.Should().Be("category");
        harness.Conversations.SnapshotOf(harness.User.Id)!.Conversation.Should().Be("expense");
        await harness.ReportService.DidNotReceive().GetMonthlySummaryAsync(
            Arg.Any<Guid>(), Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Text_that_is_not_an_expense_falls_through_to_help()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, new MonthPeriod(2026, 10), "summary"), "hola", CancellationToken.None);

        turn.Completed.Should().BeTrue();
        turn.Responses[0].Text.Should().Be(harness.Messages.Get("es", MessageKeys.Help));
        harness.Conversations.Count.Should().Be(0);
    }
}
