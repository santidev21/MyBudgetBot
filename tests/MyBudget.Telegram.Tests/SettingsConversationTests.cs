using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Localization;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Presentation;
using MyBudget.Telegram.Tests.Fakes;
using NSubstitute;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// Settings driven through the real router.
/// <para>
/// The reminder switch is a normal preference change; erasing the user is the one irreversible
/// action, so the confirmation is part of the contract and the turn must say the user is gone.
/// </para>
/// </summary>
public sealed class SettingsConversationTests
{
    private const long ChatId = 4242;

    private static ConversationContext ContextFor(TelegramHarness harness, string? state = null) =>
        new(
            harness.User,
            ChatId,
            state is null
                ? null
                : new ConversationSnapshot(
                    harness.User.Id,
                    ChatId,
                    SettingsConversation.ConversationName,
                    state,
                    "{}",
                    TestClock.Now.AddMinutes(30)));

    [Fact]
    public async Task The_settings_screen_shows_the_reminder_state_and_the_delete_button()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteTextAsync(
            ContextFor(harness), harness.Messages.Get("es", MessageKeys.MenuSettings), CancellationToken.None);

        turn.NextState.Should().Be("settings");
        turn.Responses.Should().ContainSingle();

        var message = turn.Responses[0];
        message.Text.Should().Contain(harness.Messages.Get("es", MessageKeys.SettingsReminderStateOn));
        message.Keyboard.Should().NotBeNull();
        message.Keyboard!.Rows
            .SelectMany(row => row.Select(button => button.Text))
            .Should().Contain(harness.Messages.Get("es", MessageKeys.SettingsDeleteData));
    }

    [Fact]
    public async Task Toggling_the_reminder_flips_the_preference_and_keeps_the_flow_open()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "settings"),
            new IncomingCallback("cb", "settings:reminder"),
            CancellationToken.None);

        harness.User.DailyReminderEnabled.Should().BeFalse();
        turn.NextState.Should().Be("settings");
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Contain(harness.Messages.Get("es", MessageKeys.SettingsReminderStateOff));
    }

    [Fact]
    public async Task Deleting_data_asks_for_confirmation_before_erasing()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "settings"),
            new IncomingCallback("cb", "settings:delete"),
            CancellationToken.None);

        await harness.Eraser.DidNotReceiveWithAnyArgs().EraseAsync(default);
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.SettingsDeleteWarning));
    }

    [Fact]
    public async Task Confirming_the_deletion_erases_the_user_and_marks_the_turn()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Router.RouteCallbackAsync(
            ContextFor(harness, "settings"),
            new IncomingCallback("cb", "settings:delete:confirm"),
            CancellationToken.None);

        await harness.Eraser.Received(1).EraseAsync(harness.User.Id, Arg.Any<CancellationToken>());
        turn.Completed.Should().BeTrue();
        turn.UserRemoved.Should().BeTrue();
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.SettingsDeleted));
    }
}
