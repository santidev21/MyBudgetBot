using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Budgets;
using MyBudget.Application.Categories;
using MyBudget.Application.Localization;
using MyBudget.Domain.Budgets;
using MyBudget.Domain.Categories;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The budget slice of the category flow, driven through the real router. The clock is fixed at
/// an instant that is still September in Bogotá, so the current month is deterministic.
/// </summary>
public sealed class CategoriesConversationBudgetTests
{
    private const long ChatId = 4242;
    private static readonly MonthPeriod September = new(2026, 9);
    private static readonly DateOnly Today = new(2026, 9, 28);

    private static ConversationContext ContextFor(
        TelegramHarness harness, string state, CategoriesPayload payload) =>
        new(
            harness.User,
            ChatId,
            new ConversationSnapshot(
                harness.User.Id,
                ChatId,
                CategoriesConversation.ConversationName,
                state,
                payload.Serialize(),
                TestClock.Now.AddMinutes(30)));

    private static void StubMonth(TelegramHarness harness, MonthlyBudgetView view) =>
        harness.BudgetService
            .GetMonthAsync(Arg.Any<Guid>(), Arg.Any<MonthPeriod>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(view));

    [Fact]
    public async Task The_budget_screen_shows_the_current_month_and_its_lines()
    {
        var harness = TelegramHarness.Build();
        var categoryId = Guid.NewGuid();
        StubMonth(harness, new MonthlyBudgetView(
            September,
            [new MonthlyBudgetLine(categoryId, "Mercado", "🛒", 500_000, IsActive: true)]));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "menu", new CategoriesPayload()),
            new IncomingCallback("cb", "cats:budget"),
            CancellationToken.None);

        turn.NextState.Should().Be("budget");
        var text = turn.Responses.Last().Text;
        text.Should().Contain("septiembre 2026");
        text.Should().Contain("$500.000");
        text.Should().Contain(harness.Messages.Get("es", MessageKeys.BudgetTotal, "$500.000"));
    }

    [Fact]
    public async Task An_empty_month_says_there_is_nothing_assigned()
    {
        var harness = TelegramHarness.Build();
        StubMonth(harness, new MonthlyBudgetView(September, []));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "menu", new CategoriesPayload()),
            new IncomingCallback("cb", "cats:budget"),
            CancellationToken.None);

        turn.Responses.Last().Text.Should().Contain(
            harness.Messages.Get("es", MessageKeys.BudgetEmpty));
    }

    [Fact]
    public async Task Assigning_picks_a_category_then_parses_the_amount()
    {
        var harness = TelegramHarness.Build();
        var categoryId = Guid.NewGuid();
        StubMonth(harness, new MonthlyBudgetView(
            September,
            [new MonthlyBudgetLine(categoryId, "Mercado", "🛒", 0, IsActive: true)]));
        harness.CategoryService
            .GetAsync(harness.User.Id, categoryId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<BudgetCategory?>(new BudgetCategory(harness.User.Id, "Mercado", "🛒")));
        harness.BudgetService
            .SetAllocationAsync(
                Arg.Any<Guid>(), Arg.Any<MonthPeriod>(), categoryId, 500_000, Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BudgetWriteResult.Ok(new MonthlyBudget(harness.User.Id, September))));

        var picker = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "budget", new CategoriesPayload()),
            new IncomingCallback("cb", "cats:budget:assign"),
            CancellationToken.None);

        picker.NextState.Should().Be("budget-category");
        picker.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.BudgetChooseCategory));

        var prompt = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "budget-category", new CategoriesPayload()),
            new IncomingCallback("cb", $"cats:budget:set:{categoryId}"),
            CancellationToken.None);

        prompt.NextState.Should().Be("awaiting-budget-amount");
        prompt.Responses.Last().Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.BudgetAmountPrompt, "Mercado"));
        prompt.Responses.Last().Keyboard!.Rows.SelectMany(row => row)
            .Should().Contain(button => button.CallbackData == "cats:budget", "the amount prompt goes back");

        var saved = await harness.Router.RouteTextAsync(
            ContextFor(
                harness, "awaiting-budget-amount",
                new CategoriesPayload { CategoryId = categoryId, Name = "Mercado" }),
            "500.000",
            CancellationToken.None);

        await harness.BudgetService.Received(1).SetAllocationAsync(
            harness.User.Id, September, categoryId, 500_000, Today, Arg.Any<CancellationToken>());
        saved.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.BudgetSaved, "$500.000", "Mercado"));
    }

    [Fact]
    public async Task An_invalid_amount_is_rejected_without_calling_the_service()
    {
        var harness = TelegramHarness.Build();
        var categoryId = Guid.NewGuid();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(
                harness, "awaiting-budget-amount",
                new CategoriesPayload { CategoryId = categoryId, Name = "Mercado" }),
            "abc",
            CancellationToken.None);

        turn.NextState.Should().Be("awaiting-budget-amount");
        turn.Responses[0].Text.Should().Be(
            harness.Messages.Get("es", MessageKeys.BudgetAmountInvalid));
        await harness.BudgetService.DidNotReceiveWithAnyArgs().SetAllocationAsync(
            default, default, default, default, default, default);
    }

    [Fact]
    public async Task A_past_month_refusal_is_surfaced()
    {
        var harness = TelegramHarness.Build();
        var categoryId = Guid.NewGuid();
        StubMonth(harness, new MonthlyBudgetView(September, []));
        harness.BudgetService
            .SetAllocationAsync(
                Arg.Any<Guid>(), Arg.Any<MonthPeriod>(), Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<DateOnly>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(BudgetWriteResult.PastMonth()));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(
                harness, "awaiting-budget-amount",
                new CategoriesPayload { CategoryId = categoryId, Name = "Mercado" }),
            "100.000",
            CancellationToken.None);

        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.BudgetPastMonth));
    }

    [Fact]
    public async Task Copying_the_previous_month_reports_success()
    {
        var harness = TelegramHarness.Build();
        StubMonth(harness, new MonthlyBudgetView(September, []));
        harness.BudgetService
            .CopyPreviousMonthAsync(Arg.Any<Guid>(), Arg.Any<MonthPeriod>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BudgetCopyResult(BudgetCopyStatus.Copied)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "budget", new CategoriesPayload()),
            new IncomingCallback("cb", "cats:budget:copy"),
            CancellationToken.None);

        await harness.BudgetService.Received(1).CopyPreviousMonthAsync(
            harness.User.Id, September, Today, Arg.Any<CancellationToken>());
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.BudgetCopied));
    }

    [Fact]
    public async Task Copying_without_a_previous_budget_is_explained()
    {
        var harness = TelegramHarness.Build();
        StubMonth(harness, new MonthlyBudgetView(September, []));
        harness.BudgetService
            .CopyPreviousMonthAsync(Arg.Any<Guid>(), Arg.Any<MonthPeriod>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new BudgetCopyResult(BudgetCopyStatus.NoPreviousBudget)));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "budget", new CategoriesPayload()),
            new IncomingCallback("cb", "cats:budget:copy"),
            CancellationToken.None);

        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.BudgetNoPrevious));
    }

    [Fact]
    public async Task Assigning_with_no_active_categories_explains_it()
    {
        var harness = TelegramHarness.Build();
        var inactive = new MonthlyBudgetLine(Guid.NewGuid(), "Viajes", "✈️", 0, IsActive: false);
        StubMonth(harness, new MonthlyBudgetView(September, [inactive]));

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "budget", new CategoriesPayload()),
            new IncomingCallback("cb", "cats:budget:assign"),
            CancellationToken.None);

        turn.NextState.Should().Be("budget");
        turn.Responses.Should().Contain(response =>
            response.Text == harness.Messages.Get("es", MessageKeys.BudgetNoCategories));
    }
}
