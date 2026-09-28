namespace MyBudget.Application.Abstractions.Telegram;

/// <summary>
/// The inbox that makes update processing idempotent.
/// <para>
/// Telegram retries a webhook when it does not receive a timely 2xx, and it can deliver the
/// same update more than once. Claiming an update before processing it, and marking it done
/// only afterwards, means a retry never creates a second expense.
/// </para>
/// </summary>
public interface IUpdateInbox
{
    /// <summary>
    /// Claims an update for processing. Returns <c>true</c> when the caller should process it
    /// (first delivery, or a retry after a previous failure) and <c>false</c> when it was
    /// already completed and must be answered with 200 without doing anything.
    /// </summary>
    Task<bool> TryClaimAsync(long updateId, CancellationToken cancellationToken = default);

    Task CompleteAsync(long updateId, Guid? userId, CancellationToken cancellationToken = default);

    Task FailAsync(long updateId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Records an update that was deliberately not acted on, for example a stale one.</summary>
    Task IgnoreAsync(long updateId, Guid? userId, string reason, CancellationToken cancellationToken = default);

    /// <summary>Deletes inbox rows older than the retention window. Returns the number removed.</summary>
    Task<int> PurgeOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
}
