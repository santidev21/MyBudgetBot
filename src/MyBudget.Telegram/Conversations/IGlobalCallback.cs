using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// A callback that is handled even when no conversation is active.
/// <para>
/// The undo button lives on the message that follows a completed flow, so by the time it is
/// tapped the conversation is gone. Returning <c>null</c> means "not mine": the router then
/// treats the callback as expired.
/// </para>
/// </summary>
internal interface IGlobalCallback
{
    Task<ConversationTurn?> TryHandleAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken);
}
