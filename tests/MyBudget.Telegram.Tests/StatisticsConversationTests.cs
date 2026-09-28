using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Localization;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The statistics screen, driven through the real router. The full rendered message is asserted
/// so a change to a section, its order or its wording is visible in the diff.
/// </summary>
public sealed class StatisticsConversationTests
{
    private const long ChatId = 4242;

    private static readonly MonthPeriod September = new(2026, 9);

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
                    StatisticsConversation.ConversationName,
                    state,
                    (period is null ? new ReportingPayload() : new ReportingPayload().WithPeriod(period.Value))
                        .Serialize(),
                    TestClock.Now.AddMinutes(30)));

    private static PeriodStatistics Sample()
    {
        var comparison = new PeriodComparison(
            September,
            new MonthPeriod(2026, 8),
            Total: 150_000,
            PreviousTotal: 100_000,
            PeriodIncomplete: true,
            PreviousPeriodIncomplete: false);

        return new PeriodStatistics(
            September,
            Total: 150_000,
            ExpenseCount: 5,
            DaysCounted: 28,
            AverageDaily: 5_357,
            Categories:
            [
                new CategoryShare(Guid.NewGuid(), "Comida", "🍔", 90_000, 3, 60m),
                new CategoryShare(Guid.NewGuid(), "Ocio", "🎬", 60_000, 2, 40m),
            ],
            Daily:
            [
                new DailyTotal(new DateOnly(2026, 9, 11), 15_000, 1),
                new DailyTotal(new DateOnly(2026, 9, 10), 135_000, 4),
            ],
            Largest:
            [
                new LargestExpense(Guid.NewGuid(), 90_000, "Almuerzo", new DateOnly(2026, 9, 10), "Comida", "🍔"),
                new LargestExpense(Guid.NewGuid(), 60_000, null, new DateOnly(2026, 9, 11), "Ocio", "🎬"),
            ],
            Comparison: comparison);
    }

    [Fact]
    public async Task The_statistics_render_every_section_and_warn_about_an_unfinished_month()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetStatisticsAsync(
                harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Sample());

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuStatistics), CancellationToken.None);

        turn.NextState.Should().Be("statistics");
        turn.Responses.Should().ContainSingle();
        turn.Responses[0].Text.Should().Be(
            """
            📈 Estadísticas de septiembre 2026

            Total: $150.000
            Gastos: 5
            Promedio diario: $5.357
            ⚠️ El mes está en curso: el total todavía puede cambiar.

            Por categoría:
            🍔 Comida: $90.000 (60 %)
            🎬 Ocio: $60.000 (40 %)

            Gasto diario:
            11/9: $15.000
            10/9: $135.000

            Gastos más grandes:
            • $90.000 · Almuerzo (10/9)
            • $60.000 · Sin descripción (11/9)

            Comparación:
            agosto 2026: $100.000
            septiembre 2026: $150.000
            ▲ 50 % más que agosto 2026.
            """);
    }

    [Fact]
    public async Task An_empty_month_shows_no_categories_or_comparison_change()
    {
        var harness = TelegramHarness.Build();
        var comparison = new PeriodComparison(
            September, new MonthPeriod(2026, 8), 0, 0, PeriodIncomplete: true, PreviousPeriodIncomplete: false);
        harness.ReportService
            .GetStatisticsAsync(
                harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new PeriodStatistics(September, 0, 0, 15, 0, [], [], [], comparison));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuStatistics), CancellationToken.None);

        turn.Responses[0].Text.Should().Be(
            """
            📈 Estadísticas de septiembre 2026

            Total: $0
            Gastos: 0
            Promedio diario: $0
            ⚠️ El mes está en curso: el total todavía puede cambiar.

            No hay gastos registrados en este mes.
            """);
    }

    [Fact]
    public async Task The_next_button_moves_to_the_following_month()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetStatisticsAsync(
                harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new PeriodStatistics(
                new MonthPeriod(2026, 10), 0, 0, 0, 0, [], [], [],
                new PeriodComparison(
                    new MonthPeriod(2026, 10), September, 0, 0, false, false)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, September, "statistics"),
            new IncomingCallback("cb", "stats:next"),
            CancellationToken.None);

        turn.Responses[0].Text.Should().StartWith("📈 Estadísticas de octubre 2026");
        await harness.ReportService.Received(1).GetStatisticsAsync(
            harness.User.Id, new MonthPeriod(2026, 10), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }
}
