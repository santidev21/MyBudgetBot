namespace MyBudget.Application.Matching;

/// <summary>What the matcher concluded about a free-text query (design §9).</summary>
public enum CategoryMatchOutcome
{
    /// <summary>Nothing scored high enough; the user must be asked.</summary>
    None,

    /// <summary>Several candidates are too close to choose between; the user must be asked.</summary>
    Ambiguous,

    /// <summary>A single candidate is clearly ahead; it can be suggested for confirmation.</summary>
    Matched,
}

/// <summary>
/// The names and keywords of one of the user's categories, as the pure matcher sees them.
/// <para>
/// The caller passes data, not entities: the matcher has no repository, no clock and no Telegram
/// types, which is what makes it exhaustively testable.
/// </para>
/// </summary>
public sealed record CategoryMatchInput(
    Guid CategoryId,
    string Name,
    string Icon,
    IReadOnlyList<string> Aliases);

/// <summary>One scored category, ranked by descending score.</summary>
public sealed record CategoryMatchCandidate(Guid CategoryId, string Name, string Icon, double Score);

/// <summary>
/// The matcher's decision. <see cref="Suggestion"/> is set only for <c>Matched</c>;
/// <see cref="Candidates"/> carries the plausible categories for a <c>Ambiguous</c> result.
/// </summary>
public sealed record CategoryMatchResult(
    CategoryMatchOutcome Outcome,
    CategoryMatchCandidate? Suggestion,
    IReadOnlyList<CategoryMatchCandidate> Candidates)
{
    public static CategoryMatchResult NoMatch() =>
        new(CategoryMatchOutcome.None, null, []);
}
