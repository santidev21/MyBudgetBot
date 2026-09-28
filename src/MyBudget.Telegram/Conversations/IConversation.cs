using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Domain.Users;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>A plain message from the user.</summary>
public sealed record IncomingText(string Text);

/// <summary>A tap on an inline button.</summary>
public sealed record IncomingCallback(string CallbackQueryId, string Data);

/// <summary>Everything a conversation needs to make a decision.</summary>
public sealed record ConversationContext(User User, long ChatId, ConversationSnapshot? Conversation)
{
    public string CurrentState => Conversation?.State ?? string.Empty;

    public string Language => User.Language;
}

/// <summary>
/// The outcome of one step: what to send, and what the conversation's state becomes.
/// </summary>
public sealed record ConversationTurn(IReadOnlyList<BotResponse> Responses)
{
    /// <summary>State to store, or <c>null</c> when the flow is over.</summary>
    public string? NextState { get; init; }

    /// <summary>Opaque payload for the next step.</summary>
    public string? NextPayload { get; init; }

    /// <summary>True when the flow finished and the stored state must be cleared.</summary>
    public bool Completed { get; init; }

    public static ConversationTurn Say(string text) => new([BotResponse.Message(text)]);

    public static ConversationTurn Say(string text, BotKeyboard keyboard) =>
        new([BotResponse.Message(text, keyboard)]);
}

/// <summary>
/// One explicit state machine.
/// <para>
/// Deliberately not a generic wizard framework: with a handful of flows, explicit code is
/// easier to follow, easier to test and easier to change than a framework's configuration.
/// </para>
/// </summary>
public interface IConversation
{
    /// <summary>Stable identifier persisted with the state; never shown to the user.</summary>
    string Name { get; }

    Task<ConversationTurn> StartAsync(ConversationContext context, CancellationToken cancellationToken);

    Task<ConversationTurn> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken);

    Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken);
}
