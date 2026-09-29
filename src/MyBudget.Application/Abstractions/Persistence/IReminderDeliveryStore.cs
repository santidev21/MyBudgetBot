namespace MyBudget.Application.Abstractions.Persistence;

/// <summary>
/// Remembers that a scheduled notification was already delivered to a user on one local day, so
/// a scheduler that ticks every minute cannot send the same message twice.
/// <para>
/// Operational state, not a domain entity: it says nothing about money, it only stops the bot
/// from repeating itself. The claimant writes the row with an upsert (<c>ON CONFLICT DO
/// NOTHING</c>) and the insert decides: whoever wins the race is the one that sends.
/// </para>
/// </summary>
public interface IReminderDeliveryStore
{
    /// <summary>
    /// Claims the right to send one notification of <paramref name="kind"/> to one user for their
    /// local <paramref name="localDate"/>.
    /// <para>
    /// Returns <c>true</c> only for the first caller. A later call for the same user, kind and
    /// day returns <c>false</c>, which is what makes the delivery exactly-once across restarts
    /// and concurrent passes.
    /// </para>
    /// </summary>
    Task<bool> TryClaimAsync(
        Guid userId,
        string kind,
        DateOnly localDate,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken = default);
}
