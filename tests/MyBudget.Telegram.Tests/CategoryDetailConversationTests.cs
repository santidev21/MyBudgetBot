using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Expenses;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The summary's per-category breakdown, driven through the real router.
/// <para>
/// Opening a movement must hand off to the expenses flow, which owns the detail and the edit and
/// delete actions, rather than reimplementing them.
/// </para>
/// </summary>
public sealed class CategoryDetailConversationTests
{
    private const long ChatId = 4242;
    private static readonly MonthPeriod September = new(2026, 9);
    private static readonly Guid CategoryId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ExpenseId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static ConversationContext ContextFor(
        TelegramHarness harness, string conversation, string state, string payload = "{}") =>
        new(
            harness.User,
            ChatId,
            new ConversationSnapshot(
                harness.User.Id, ChatId, conversation, state, payload, TestClock.Now.AddMinutes(30)));

    private static IReadOnlyList<CategoryShare> Shares() =>
        [new CategoryShare(CategoryId, "Comida", "🍔", 700_000, 2, 100m)];

    private static ExpenseListItem Item() =>
        new(ExpenseId, CategoryId, "Comida", "🍔", 300_000, "Almuerzo", new DateOnly(2026, 9, 10),
            CategorizationSource.Manual);

    [Fact]
    public async Task The_summary_button_hands_off_to_the_category_breakdown()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetStatisticsAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new PeriodStatistics(
                September, 700_000, 2, 30, 0, Shares(), [], [],
                new PeriodComparison(September, September.Previous, 700_000, 0, true, false)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, SummaryConversation.ConversationName, "summary"),
            new IncomingCallback("cb", CategoryDetailConversation.OpenCallback),
            CancellationToken.None);

        turn.NextState.Should().Be("choose");
        harness.Conversations.SnapshotOf(harness.User.Id)!.Conversation
            .Should().Be(CategoryDetailConversation.ConversationName);
        turn.Responses[0].Text.Should().Be(harness.Messages.Get("es", MessageKeys.BreakdownChoose));
    }

    [Fact]
    public async Task Picking_a_category_lists_its_movements_for_the_month()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetStatisticsAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new PeriodStatistics(
                September, 700_000, 2, 30, 0, Shares(), [], [],
                new PeriodComparison(September, September.Previous, 700_000, 0, true, false)));
        harness.ReportService
            .GetHistoryAsync(
                harness.User.Id, Arg.Any<DateRange>(), null, Arg.Any<int>(), CategoryId,
                Arg.Any<CancellationToken>())
            .Returns(new ExpenseHistoryPage(DateRange.ForMonth(September), [Item()], null));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, CategoryDetailConversation.ConversationName, "choose"),
            new IncomingCallback("cb", "catdetail:cat:" + CategoryId),
            CancellationToken.None);

        turn.NextState.Should().Be("movements");
        turn.Responses[0].Text.Should().Contain("Comida");
        turn.Responses[0].Text.Should().Contain("Almuerzo");
    }

    [Fact]
    public async Task Opening_a_movement_hands_it_to_the_expenses_flow()
    {
        var harness = TelegramHarness.Build();
        harness.ExpenseService
            .GetItemAsync(harness.User.Id, ExpenseId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<ExpenseListItem?>(Item()));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, CategoryDetailConversation.ConversationName, "movements"),
            new IncomingCallback("cb", "catdetail:exp:" + ExpenseId),
            CancellationToken.None);

        turn.NextState.Should().Be("detail");
        harness.Conversations.SnapshotOf(harness.User.Id)!.Conversation
            .Should().Be(ExpensesConversation.ConversationName);
    }

    [Fact]
    public async Task The_chooser_always_offers_a_way_back_to_the_summary()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetStatisticsAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(new PeriodStatistics(
                September, 700_000, 2, 30, 0, Shares(), [], [],
                new PeriodComparison(September, September.Previous, 700_000, 0, true, false)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, SummaryConversation.ConversationName, "summary"),
            new IncomingCallback("cb", CategoryDetailConversation.OpenCallback),
            CancellationToken.None);

        var back = turn.Responses[0].Keyboard!.Rows[^1].Single();

        back.CallbackData.Should().Be(CategoryDetailConversation.BackToSummaryCallback);
        back.Text.Should().Be(harness.Messages.Get("es", MessageKeys.ButtonBack));
    }

    [Fact]
    public async Task Going_back_from_the_chooser_resumes_the_summary()
    {
        var harness = TelegramHarness.Build();
        harness.ReportService
            .GetMonthlySummaryAsync(harness.User.Id, Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(new MonthlySummary(September, []));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, CategoryDetailConversation.ConversationName, "choose"),
            new IncomingCallback("cb", CategoryDetailConversation.BackToSummaryCallback),
            CancellationToken.None);

        harness.Conversations.SnapshotOf(harness.User.Id)!.Conversation
            .Should().Be(SummaryConversation.ConversationName);
        turn.Responses[0].Text.Should().StartWith(
            harness.Messages.Get("es", MessageKeys.SummaryHeader, "septiembre 2026"));
    }
}
