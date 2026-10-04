using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Localization;
using MyBudget.Domain.Categories;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Presentation;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

public sealed class ConversationRouterTests
{
    private const long ChatId = 4242;

    private static ConversationContext ContextFor(
        TelegramHarness harness, string? state = null, string conversation = StartConversation.ConversationName) =>
        new(
            harness.User,
            ChatId,
            state is null
                ? null
                : new ConversationSnapshot(
                    harness.User.Id, ChatId, conversation, state, "{}", TestClock.Now.AddMinutes(30)));

    [Fact]
    public async Task Start_persists_the_onboarding_state()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(ContextFor(harness), "/start", CancellationToken.None);

        turn.NextState.Should().Be("awaiting-timezone");
        turn.Completed.Should().BeFalse();
        harness.Conversations.SnapshotOf(harness.User.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task Cancel_clears_whatever_was_in_progress()
    {
        var harness = TelegramHarness.Build();
        await harness.Router.RouteTextAsync(ContextFor(harness), "/start", CancellationToken.None);

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-timezone"), "/cancel", CancellationToken.None);

        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.Cancelled));
        harness.Conversations.Count.Should().Be(0);
    }

    [Fact]
    public async Task A_command_is_recognised_even_with_a_bot_suffix()
    {
        var harness = TelegramHarness.Build();

        await harness.Router.RouteTextAsync(ContextFor(harness), "/start@MyBudgetBot", CancellationToken.None);

        harness.Conversations.SnapshotOf(harness.User.Id).Should().NotBeNull();
    }

    [Fact]
    public async Task Help_ends_any_flow_and_shows_the_menu()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-timezone"), "/help", CancellationToken.None);

        turn.Completed.Should().BeTrue();
        harness.Conversations.Count.Should().Be(0);
        turn.Responses.Should().ContainSingle();
        turn.Responses[0].Keyboard.Should().NotBeNull();
        turn.Responses[0].Keyboard!.Kind.Should().Be(BotKeyboardKind.Reply);
    }

    [Fact]
    public async Task An_unknown_command_falls_back_to_help()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(ContextFor(harness), "/nonsense", CancellationToken.None);

        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.Help));
    }

    [Fact]
    public async Task Text_belonging_to_an_active_flow_is_delegated()
    {
        var harness = TelegramHarness.Build();
        await harness.Router.RouteTextAsync(ContextFor(harness), "/start", CancellationToken.None);

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, "awaiting-custom-timezone"), "America/Bogota", CancellationToken.None);

        turn.Completed.Should().BeTrue();
        harness.User.TimeZone.Should().Be("America/Bogota");
    }

    [Fact]
    public async Task A_callback_for_an_unregistered_flow_is_treated_as_expired()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "awaiting-timezone", conversation: "deleted-flow"),
            new IncomingCallback("cb", "tz:America/Bogota"),
            CancellationToken.None);

        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.ConversationExpired));
    }

    [Fact]
    public async Task A_settings_tap_opens_the_settings_screen()
    {
        var harness = TelegramHarness.Build();
        var label = harness.Messages.Get("es", MessageKeys.MenuSettings);

        var turn = await harness.Router.RouteTextAsync(ContextFor(harness), label, CancellationToken.None);

        turn.Completed.Should().BeFalse();
        turn.NextState.Should().Be("settings");
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Contain(harness.Messages.Get("es", MessageKeys.SettingsTitle));
    }

    [Theory]
    [InlineData(CategoriesConversation.ConversationName, "menu")]
    [InlineData(ExpensesConversation.ConversationName, "list")]
    [InlineData(RecurringConversation.ConversationName, "list")]
    [InlineData(SummaryConversation.ConversationName, "summary")]
    [InlineData(StatisticsConversation.ConversationName, "statistics")]
    [InlineData(CategoryDetailConversation.ConversationName, "choose")]
    [InlineData(SettingsConversation.ConversationName, "settings")]
    public async Task A_compact_expense_passes_through_a_screen_that_only_owns_buttons(
        string conversation, string state)
    {
        var harness = TelegramHarness.Build();
        var food = new BudgetCategory(harness.User.Id, "Comida", "🍔");
        harness.CategoryService
            .ListAsync(harness.User.Id, false, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<BudgetCategory>>([food]));

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness, state, conversation), "115509 carnes colanta", CancellationToken.None);

        // The screen must not swallow the message: it becomes the expense flow.
        turn.NextState.Should().Be("category");
        harness.Conversations.SnapshotOf(harness.User.Id)!.Conversation.Should().Be("expense");
    }
}
