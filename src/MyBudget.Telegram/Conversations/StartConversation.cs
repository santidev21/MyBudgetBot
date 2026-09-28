using Microsoft.Extensions.Options;
using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Localization;
using MyBudget.Telegram.Options;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// First contact: welcome, confirm the time zone, then show the menu.
/// <para>
/// Categories and budgets are not created here. They belong with the flows that let the user
/// manage them, so onboarding stays a two-step introduction instead of a wizard.
/// </para>
/// </summary>
internal sealed class StartConversation(
    IUserMessages messages,
    MainMenu menu,
    IUnitOfWork unitOfWork) : IConversation
{
    public const string ConversationName = "start";

    private const string AwaitingTimezone = "awaiting-timezone";
    private const string AwaitingCustomTimezone = "awaiting-custom-timezone";
    private const string TimezoneCallbackPrefix = "tz:";

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(ConversationContext context, CancellationToken cancellationToken)
    {
        var language = context.Language;

        var turn = new ConversationTurn(
        [
            BotResponse.Message(messages.Get(language, MessageKeys.Welcome)),
            BotResponse.Message(messages.Get(language, MessageKeys.OnboardingTimezonePrompt), TimezoneKeyboard()),
        ])
        {
            NextState = AwaitingTimezone,
            NextPayload = "{}",
        };

        return Task.FromResult(turn);
    }

    public Task<ConversationTurn> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken)
    {
        var language = context.Language;

        if (context.CurrentState != AwaitingCustomTimezone)
        {
            return Task.FromResult(TimeZonePrompt(language, AwaitingTimezone));
        }

        var candidate = text.Text.Trim();

        if (TimeZoneInfo.TryFindSystemTimeZoneById(candidate, out _))
        {
            return ConfirmAsync(context, candidate, cancellationToken);
        }

        return Task.FromResult(new ConversationTurn(
        [
            BotResponse.Message(messages.Get(language, MessageKeys.OnboardingTimezoneCustomInvalid)),
            BotResponse.Message(messages.Get(language, MessageKeys.OnboardingTimezoneCustomPrompt)),
        ])
        {
            NextState = AwaitingCustomTimezone,
            NextPayload = "{}",
        });
    }

    public Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        var language = context.Language;

        if (!callback.Data.StartsWith(TimezoneCallbackPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(TimeZonePrompt(language, AwaitingTimezone));
        }

        var value = callback.Data[TimezoneCallbackPrefix.Length..];

        if (value == "custom")
        {
            return Task.FromResult(new ConversationTurn(
                [BotResponse.Message(messages.Get(language, MessageKeys.OnboardingTimezoneCustomPrompt))])
            {
                NextState = AwaitingCustomTimezone,
                NextPayload = "{}",
            });
        }

        if (!TimeZoneInfo.TryFindSystemTimeZoneById(value, out _))
        {
            // A button we do not recognise: ask again rather than storing nonsense.
            return Task.FromResult(TimeZonePrompt(language, AwaitingTimezone));
        }

        return ConfirmAsync(context, value, cancellationToken);
    }

    private async Task<ConversationTurn> ConfirmAsync(
        ConversationContext context, string timeZoneId, CancellationToken cancellationToken)
    {
        var language = context.Language;

        context.User.ChangeTimeZone(timeZoneId);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new ConversationTurn(
        [
            BotResponse.Message(messages.Get(language, MessageKeys.OnboardingTimezoneSaved)),
            BotResponse.Message(messages.Get(language, MessageKeys.Help), menu.ReplyKeyboard(language)),
        ])
        {
            Completed = true,
        };
    }

    private ConversationTurn TimeZonePrompt(string language, string state) =>
        new([BotResponse.Message(messages.Get(language, MessageKeys.OnboardingTimezonePrompt), TimezoneKeyboard())])
        {
            NextState = state,
            NextPayload = "{}",
        };

    private static BotKeyboard TimezoneKeyboard()
    {
        var rows = TimezoneChoices.Common
            .Select(choice => new[] { new BotButton(choice.Label, TimezoneCallbackPrefix + choice.TimeZoneId) })
            .ToList();

        rows.Add([new BotButton("🌎 Otra zona horaria", TimezoneChoices.CustomCallbackData)]);

        return BotKeyboard.Inline([.. rows]);
    }
}
