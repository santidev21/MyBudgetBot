namespace MyBudget.Application.Abstractions.Telegram;

/// <summary>
/// Serialises the work of a single user.
/// <para>
/// Telegram can deliver two updates from the same chat at once, and processes that read state,
/// decide and write are not safe under that. Holding an exclusive database lock for the
/// duration of one user's update makes the decision and the write atomic, without making
/// different users wait for each other.
/// </para>
/// </summary>
public interface IUserWorkLock
{
    /// <summary>
    /// Runs <paramref name="work"/> while holding an exclusive lock for the user, inside one
    /// transaction. The work is committed before the result is returned, so callers can send
    /// their reply knowing the state change is durable.
    /// </summary>
    Task<TResult> ExecuteAsync<TResult>(
        Guid userId,
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default);
}
