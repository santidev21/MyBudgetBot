using FluentAssertions;
using MyBudget.Telegram.Options;
using MyBudget.Telegram.Tests;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;
using Telegram.Bot.Types.Enums;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// The pipeline gates. These are the properties that keep a personal bot personal and keep
/// Telegram's retries from duplicating anything.
/// </summary>
public sealed class TelegramUpdateDispatcherTests
{
    [Fact]
    public async Task A_start_command_creates_the_conversation_and_marks_the_update_processed()
    {
        var harness = TelegramHarness.Build();

        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(1, 999, "/start"));

        harness.Inbox.StatusOf(1).Should().Be("processed");
        harness.Sender.Messages.Should().HaveCount(2);
        harness.Sender.Messages[0].Text.Should().Contain("presupuesto");

        var snapshot = harness.Conversations.SnapshotOf(harness.User.Id);
        snapshot.Should().NotBeNull();
        snapshot!.Conversation.Should().Be("start");
        snapshot.State.Should().Be("awaiting-timezone");
    }

    [Fact]
    public async Task A_duplicate_delivery_is_answered_without_acting_twice()
    {
        var harness = TelegramHarness.Build();
        var update = TestUpdates.PrivateMessage(7, 999, "/start");

        await harness.Dispatcher.DispatchAsync(update);
        await harness.Dispatcher.DispatchAsync(update);
        await harness.Dispatcher.DispatchAsync(update);

        // Telegram retries on any non-2xx; exactly one turn must reach the user.
        harness.Sender.Messages.Should().HaveCount(2);
        harness.Inbox.ClaimCount.Should().Be(3);
    }

    [Fact]
    public async Task A_group_chat_is_ignored()
    {
        var harness = TelegramHarness.Build();

        await harness.Dispatcher.DispatchAsync(
            TestUpdates.PrivateMessage(1, 999, "/start", chatType: ChatType.Group));

        harness.Sender.Messages.Should().BeEmpty();
        harness.Inbox.StatusOf(1).Should().Be("ignored");
    }

    [Fact]
    public async Task A_user_outside_the_allowlist_is_refused_and_never_created()
    {
        var harness = TelegramHarness.Build(telegramUserId: 999);

        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(1, 111, "/start"));

        harness.Sender.Messages.Should().ContainSingle()
            .Which.Text.Should().Be("Este bot es de uso privado.");

        await harness.Users.DidNotReceive()
            .GetOrCreateAsync(Arg.Any<long>(), Arg.Any<string?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>());

        harness.Inbox.StatusOf(1).Should().Be("ignored");
    }

    [Fact]
    public async Task A_stale_update_is_ignored_silently()
    {
        // After downtime Telegram replays queued updates. Answering each one would flood the
        // user with explanations for messages they sent days ago.
        var harness = TelegramHarness.Build();

        await harness.Dispatcher.DispatchAsync(
            TestUpdates.PrivateMessage(1, 999, "35.000 verduras", sentAt: TestClock.Now.UtcDateTime.AddHours(-4)));

        harness.Sender.Messages.Should().BeEmpty();
        harness.Inbox.StatusOf(1).Should().Be("ignored");
    }

    [Fact]
    public async Task An_update_with_no_chat_is_ignored()
    {
        var harness = TelegramHarness.Build();

        await harness.Dispatcher.DispatchAsync(TestUpdates.Unsupported(1, 999));

        harness.Sender.Messages.Should().BeEmpty();
        harness.Inbox.StatusOf(1).Should().Be("ignored");
    }

    [Fact]
    public async Task A_message_without_text_is_ignored()
    {
        var harness = TelegramHarness.Build();

        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(1, 999, text: null));

        harness.Sender.Messages.Should().BeEmpty();
        harness.Inbox.StatusOf(1).Should().Be("ignored");
    }

    [Fact]
    public async Task A_callback_completes_onboarding_and_stops_the_spinner()
    {
        var harness = TelegramHarness.Build();

        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(1, 999, "/start"));
        harness.Sender.Messages.Clear();

        await harness.Dispatcher.DispatchAsync(TestUpdates.Callback(2, 999, "tz:America/Bogota"));

        harness.Sender.AcknowledgedCallbacks.Should().ContainSingle().Which.Should().Be("callback-2");
        harness.User.TimeZone.Should().Be("America/Bogota");
        harness.UnitOfWork.SaveCount.Should().Be(1);

        // The flow is over, so no state is left behind to resume.
        harness.Conversations.Count.Should().Be(0);
        harness.Sender.Messages.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_callback_with_no_active_conversation_explains_that_it_expired()
    {
        var harness = TelegramHarness.Build();

        await harness.Dispatcher.DispatchAsync(TestUpdates.Callback(2, 999, "tz:America/Bogota"));

        harness.Sender.Messages.Should().ContainSingle()
            .Which.Text.Should().Contain("expiró");
    }

    [Fact]
    public async Task A_menu_tap_is_acknowledged_as_not_ready_yet()
    {
        var harness = TelegramHarness.Build();
        var label = harness.Messages.Get("es", MyBudget.Application.Localization.MessageKeys.MenuSettings);

        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(1, 999, label));

        harness.Sender.Messages.Should().ContainSingle()
            .Which.Text.Should().Contain("todavía no está lista");
    }

    [Fact]
    public async Task Free_text_with_no_active_flow_shows_help_and_the_menu()
    {
        var harness = TelegramHarness.Build();

        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(1, 999, "hola"));

        harness.Sender.Messages.Should().ContainSingle()
            .Which.Text.Should().Contain("Usa el menú");
    }

    [Fact]
    public async Task The_reply_is_sent_to_the_chat_the_update_came_from()
    {
        var harness = TelegramHarness.Build();

        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(1, 999, "/start", chatId: 555));

        harness.Sender.Messages.Should().OnlyContain(message => message.ChatId == 555);
    }

    [Fact]
    public async Task Updates_beyond_the_per_user_budget_are_dropped_with_a_single_notice()
    {
        var harness = TelegramHarness.Build(options: OptionsWithRateLimit(2));

        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(1, 999, "hola"));
        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(2, 999, "hola"));
        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(3, 999, "hola"));
        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(4, 999, "hola"));

        harness.Inbox.StatusOf(1).Should().Be("processed");
        harness.Inbox.StatusOf(2).Should().Be("processed");
        harness.Inbox.StatusOf(3).Should().Be("ignored");
        harness.Inbox.StatusOf(4).Should().Be("ignored");

        // Two normal replies, then one notice: the fourth update is dropped silently.
        harness.Sender.Messages.Should().HaveCount(3);
        harness.Sender.Messages[^1].Text.Should().Contain("Vas muy rápido");
    }

    [Fact]
    public async Task A_replayed_stale_update_does_not_spend_the_budget_a_current_message_needs()
    {
        // Telegram replays queued updates after downtime; the throttle runs after the stale
        // gate so a backlog of old messages cannot lock the user out of their next real one.
        var harness = TelegramHarness.Build(options: OptionsWithRateLimit(1));

        await harness.Dispatcher.DispatchAsync(
            TestUpdates.PrivateMessage(1, 999, "hola", sentAt: TestClock.Now.UtcDateTime.AddHours(-4)));
        await harness.Dispatcher.DispatchAsync(TestUpdates.PrivateMessage(2, 999, "hola"));

        harness.Inbox.StatusOf(1).Should().Be("ignored");
        harness.Inbox.StatusOf(2).Should().Be("processed");
    }

    private static TelegramOptions OptionsWithRateLimit(int perMinute) => new()
    {
        BotToken = "test-token",
        WebhookSecret = "test-webhook-secret-value",
        WebhookPath = "test-webhook-path-value",
        PublicBaseUrl = "https://example.test",
        AllowedUserIds = "999",
        UserRateLimitPerMinute = perMinute,
    };
}
