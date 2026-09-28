namespace MyBudget.Application.Abstractions.Telegram;

/// <summary>
/// A draft waiting for one confirmation, stored server side.
/// <para>
/// It exists so a confirmation callback can carry only its identifier
/// (<c>v1|action|&lt;id&gt;</c>), never the amount, category or date. Telegram caps callback data
/// at 64 bytes, and more importantly a callback is attacker-controlled input: the draft is
/// re-read, ownership-checked and consumed exactly once before anything is acted on.
/// </para>
/// </summary>
public sealed record PendingAction(
    Guid Id,
    Guid UserId,
    string Action,
    string Payload,
    DateTimeOffset ExpiresAt);

/// <summary>Storage for pending action drafts. Operational, never a domain entity.</summary>
public interface IPendingActionStore
{
    Task CreateAsync(PendingAction action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Claims the draft for this user exactly once and returns it, or returns <c>null</c> when it
    /// is unknown, already consumed, expired or owned by somebody else. The ownership check is
    /// part of the atomic claim, so a forged callback can never read another user's draft.
    /// </summary>
    Task<PendingAction?> ConsumeAsync(
        Guid userId, Guid actionId, CancellationToken cancellationToken = default);
}
