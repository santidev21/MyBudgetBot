namespace MyBudget.Infrastructure.Persistence.Records;

/// <summary>
/// Inbox row for an incoming Telegram update.
/// <para>
/// Operational rather than domain: it has a natural key assigned by Telegram and no behaviour,
/// so it is deliberately not an <c>Entity</c>.
/// </para>
/// </summary>
internal sealed class TelegramUpdateRecord
{
    public required long UpdateId { get; set; }

    public Guid? UserId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public DateTimeOffset? ProcessedAt { get; set; }

    public string Status { get; set; } = TelegramUpdateStatus.Received;

    public int Attempts { get; set; }

    public string? LastError { get; set; }
}

internal static class TelegramUpdateStatus
{
    public const string Received = "received";
    public const string Processed = "processed";
    public const string Failed = "failed";
    public const string Ignored = "ignored";
}

/// <summary>
/// The active conversation of a user. One row per user: a new flow replaces the previous one.
/// </summary>
internal sealed class ConversationStateRecord
{
    public Guid UserId { get; set; }

    public long ChatId { get; set; }

    public string Conversation { get; set; } = string.Empty;

    public string State { get; set; } = string.Empty;

    /// <summary>Opaque to the database: the presentation layer owns its shape.</summary>
    public string Payload { get; set; } = "{}";

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A draft waiting for one confirmation from the user. Consumed exactly once, so a replayed
/// callback cannot act twice.
/// </summary>
internal sealed class PendingActionRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Which flow the draft belongs to, for example <c>expense</c>.</summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>Opaque to the database: the presentation layer owns its shape.</summary>
    public string Payload { get; set; } = "{}";

    public DateTimeOffset? ConsumedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
