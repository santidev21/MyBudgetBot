using MyBudget.Domain.Budgets;

namespace MyBudget.Application.Abstractions.Persistence;

/// <summary>
/// Remembers that the closing report of a month was already sent to a user, so a restart or a
/// second pass the same day cannot send it twice.
/// <para>
/// Operational state, not a domain entity: it says nothing about money, it only stops the bot
/// from repeating itself. The claimant writes the row with an upsert (<c>ON CONFLICT DO
/// NOTHING</c>) and the insert decides: whoever wins the race is the one that sends.
/// </para>
/// </summary>
public interface IMonthlyClosingStore
{
    /// <summary>
    /// Claims the right to send <paramref name="period"/>'s closing to one user.
    /// <para>
    /// Returns <c>true</c> only for the first caller. A later call for the same user and month
    /// returns <c>false</c>, which is what makes the delivery exactly-once across restarts and
    /// concurrent passes.
    /// </para>
    /// </summary>
    Task<bool> TryClaimAsync(
        Guid userId,
        MonthPeriod period,
        DateTimeOffset claimedAt,
        CancellationToken cancellationToken = default);
}
