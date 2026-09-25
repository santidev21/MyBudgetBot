namespace MyBudget.Domain.Common;

/// <summary>
/// Bounds on monetary amounts, shared by the domain and by input parsing so a value the
/// parser accepts is always a value the database accepts.
/// </summary>
public static class MoneyLimits
{
    /// <summary>
    /// Largest amount the system accepts, expressed in the currency's minor unit.
    /// Mirrors the <c>ck_expenses_amount</c> database constraint.
    /// </summary>
    public const long MaxAmount = 999_999_999_999L;
}
