namespace MyBudget.Application.Abstractions.Telegram;

/// <summary>
/// The active conversation of a user, as persisted.
/// <para>
/// <paramref name="Payload"/> is opaque to this layer: the presentation layer owns its shape,
/// which keeps conversation design out of the database schema.
/// </para>
/// </summary>
public sealed record ConversationSnapshot(
    Guid UserId,
    long ChatId,
    string Conversation,
    string State,
    string Payload,
    DateTimeOffset ExpiresAt);

/// <summary>
/// Stores conversation state in PostgreSQL rather than in memory, so a restart or a deploy
/// does not leave a user stranded mid-flow.
/// </summary>
public interface IConversationStore
{
    Task<ConversationSnapshot?> FindAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SaveAsync(ConversationSnapshot conversation, CancellationToken cancellationToken = default);

    Task ClearAsync(Guid userId, CancellationToken cancellationToken = default);
}
