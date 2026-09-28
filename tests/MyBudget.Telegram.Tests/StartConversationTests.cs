using FluentAssertions;
using MyBudget.Application.Localization;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Tests.Fakes;

namespace MyBudget.Telegram.Tests;

public sealed class StartConversationTests
{
    private const long ChatId = 4242;

    private static ConversationContext ContextFor(TelegramHarness harness, string? state = null) =>
        new(
            harness.User,
            ChatId,
            state is null
                ? null
                : new Application.Abstractions.Telegram.ConversationSnapshot(
                    harness.User.Id, ChatId, StartConversation.ConversationName, state, "{}",
                    TestClock.Now.AddMinutes(30)));

    [Fact]
    public async Task Starting_welcomes_the_user_and_asks_for_the_time_zone()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Onboarding.StartAsync(ContextFor(harness), CancellationToken.None);

        turn.Responses.Should().HaveCount(2);
        turn.Responses[0].Text.Should().Contain("presupuesto");
        turn.Responses[1].Text.Should().Be(harness.Messages.Get("es", MessageKeys.OnboardingTimezonePrompt));
        turn.Responses[1].Keyboard.Should().NotBeNull();
        turn.NextState.Should().Be("awaiting-timezone");
    }

    [Fact]
    public async Task Choosing_a_common_time_zone_saves_it_and_ends_the_flow()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Onboarding.HandleCallbackAsync(
            ContextFor(harness, "awaiting-timezone"),
            new IncomingCallback("cb", "tz:America/Mexico_City"),
            CancellationToken.None);

        harness.User.TimeZone.Should().Be("America/Mexico_City");
        harness.UnitOfWork.SaveCount.Should().Be(1);
        turn.Completed.Should().BeTrue();
        turn.Responses.Should().HaveCount(2);
    }

    [Fact]
    public async Task Asking_for_another_time_zone_switches_to_free_text()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Onboarding.HandleCallbackAsync(
            ContextFor(harness, "awaiting-timezone"),
            new IncomingCallback("cb", TimezoneChoices.CustomCallbackData),
            CancellationToken.None);

        turn.NextState.Should().Be("awaiting-custom-timezone");
        turn.Responses.Should().ContainSingle()
            .Which.Text.Should().Be(harness.Messages.Get("es", MessageKeys.OnboardingTimezoneCustomPrompt));
    }

    [Fact]
    public async Task A_typed_time_zone_identifier_is_accepted()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Onboarding.HandleTextAsync(
            ContextFor(harness, "awaiting-custom-timezone"),
            new IncomingText("  America/Argentina/Buenos_Aires  "),
            CancellationToken.None);

        harness.User.TimeZone.Should().Be("America/Argentina/Buenos_Aires");
        turn.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task A_typed_value_that_is_not_a_time_zone_is_rejected()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Onboarding.HandleTextAsync(
            ContextFor(harness, "awaiting-custom-timezone"),
            new IncomingText("Bogota"),
            CancellationToken.None);

        harness.User.TimeZone.Should().Be("America/Bogota", "the default must survive an invalid answer");
        harness.UnitOfWork.SaveCount.Should().Be(0);
        turn.NextState.Should().Be("awaiting-custom-timezone");
        turn.Responses.Should().HaveCount(2);
        turn.Responses[0].Text.Should().Be(harness.Messages.Get("es", MessageKeys.OnboardingTimezoneCustomInvalid));
    }

    [Fact]
    public async Task An_unknown_time_zone_button_asks_again_instead_of_storing_nonsense()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Onboarding.HandleCallbackAsync(
            ContextFor(harness, "awaiting-timezone"),
            new IncomingCallback("cb", "tz:Nowhere/Fake"),
            CancellationToken.None);

        harness.UnitOfWork.SaveCount.Should().Be(0);
        turn.NextState.Should().Be("awaiting-timezone");
    }

    [Fact]
    public async Task A_callback_that_is_not_about_time_zones_asks_again()
    {
        var harness = TelegramHarness.Build();

        var turn = await harness.Onboarding.HandleCallbackAsync(
            ContextFor(harness, "awaiting-timezone"),
            new IncomingCallback("cb", "something-else"),
            CancellationToken.None);

        turn.NextState.Should().Be("awaiting-timezone");
        turn.Responses.Should().ContainSingle();
    }
}
