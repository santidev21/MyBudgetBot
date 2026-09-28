namespace MyBudget.Application.Reporting;

/// <summary>
/// The keyset position of an expense in the history: the date and id of the last row of the
/// previous page. Ordering is <c>(expense_date DESC, id DESC)</c>, so the next page is strictly
/// older in that order.
/// </summary>
public readonly record struct ExpensePageCursor(DateOnly ExpenseDate, Guid ExpenseId);

/// <summary>Money spent in one category over a period, plus how many expenses it took.</summary>
public sealed record CategoryTotal(Guid CategoryId, long Total, int Count);

/// <summary>Money spent on one calendar day of a period, plus how many expenses it took.</summary>
public sealed record DailyTotal(DateOnly Date, long Total, int Count);
