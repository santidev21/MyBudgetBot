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

/// <summary>
/// One budget threshold already announced for a category in a month.
/// <para>
/// Operational, not financial: it only stops the bot from repeating itself. The unique key is
/// <c>(user_id, category_id, year, month, threshold)</c>, so a race can at worst be ignored.
/// </para>
/// </summary>
internal sealed class BudgetAlertRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid CategoryId { get; set; }

    public short Year { get; set; }

    public short Month { get; set; }

    /// <summary>The percentage threshold that was crossed: 80 or 100.</summary>
    public short Threshold { get; set; }

    public DateTimeOffset NotifiedAt { get; set; }
}

/// <summary>
/// One month's closing report already sent to a user.
/// <para>
/// Operational, not financial: it only makes the delivery exactly-once. The unique key is
/// <c>(user_id, year, month)</c> over the closed month, so a restart on the first day of the
/// next month cannot send the same closing twice.
/// </para>
/// </summary>
internal sealed class MonthlyClosingRecord
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public short Year { get; set; }

    public short Month { get; set; }

    public DateTimeOffset SentAt { get; set; }
}
