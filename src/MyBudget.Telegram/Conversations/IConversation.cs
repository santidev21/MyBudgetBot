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

    /// <summary>
    /// True when the turn erased the user. The dispatcher then settles the inbox without
    /// referencing an owner that no longer exists.
    /// </summary>
    public bool UserRemoved { get; init; }

    /// <summary>
    /// When set, the router starts this conversation instead of keeping the current one. Used to
    /// hand a specific row to the flow that already knows how to edit and delete it.
    /// </summary>
    public string? HandoffConversation { get; init; }

    /// <summary>Opaque value passed to a handoff target that supports one.</summary>
    public string? HandoffPayload { get; init; }

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

    /// <summary>
    /// Handles a plain message. Returns <c>null</c> when the text is not this flow's input, so a
    /// screen that only owns callbacks never swallows a free-text entry; the router then falls
    /// through to the compact-expense parsing.
    /// </summary>
    Task<ConversationTurn?> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken);

    Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken);
}

/// <summary>
/// A conversation that can be opened on a specific row, not just from the top. The router uses
/// this when another flow hands it a value such as an expense id.
/// </summary>
internal interface IHandoffConversation
{
    Task<ConversationTurn> StartWithAsync(
        ConversationContext context, string payload, CancellationToken cancellationToken);
}

