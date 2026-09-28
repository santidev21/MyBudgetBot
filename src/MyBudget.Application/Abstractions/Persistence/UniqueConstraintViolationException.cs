namespace MyBudget.Application.Abstractions.Persistence;

/// <summary>
/// Raised when the database rejects a write because of a unique constraint.
/// <para>
/// Translating the provider's error into this type at the unit-of-work boundary is what keeps
/// <c>MyBudget.Application</c> free of Npgsql. It also gives callers a reusable way to react
/// to a duplicate: retry after a read, or turn it into a friendly message.
/// </para>
/// </summary>
public sealed class UniqueConstraintViolationException(string constraintName, Exception innerException)
    : Exception($"Unique constraint '{constraintName}' was violated.", innerException)
{
    /// <summary>The database constraint name, for example <c>uq_users_telegram_user_id</c>.</summary>
    public string ConstraintName { get; } = constraintName;
}
