namespace MyBudget.Domain.Expenses;

/// <summary>
/// How the category of an expense was chosen. Recorded so suggestion quality can be measured
/// later without logging any user text.
/// </summary>
public enum CategorizationSource
{
    /// <summary>The user picked the category themselves.</summary>
    Manual = 0,

    /// <summary>The matcher made a confident suggestion and the user accepted it.</summary>
    Matched = 1,

    /// <summary>The matcher found several candidates and the user chose among them.</summary>
    Ambiguous = 2,

    /// <summary>The user saved a new keyword for a category and it matched afterwards.</summary>
    Learned = 3,
}
