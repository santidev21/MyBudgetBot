namespace MyBudget.Application.Matching;

/// <summary>
/// Tuning for the deterministic category matcher (design §9).
/// <para>
/// The thresholds default to the documented values. Fuzzy matching is off by default because a
/// wrong category silently corrupts reports, and asking costs two seconds. This is the one knob
/// the deployment may reasonably change; the rest exist so the policy is readable and validated
/// at startup rather than buried in arithmetic.
/// </para>
/// </summary>
public sealed class CategoryMatchingOptions
{
    public const string SectionName = "CategoryMatching";

    /// <summary>
    /// When enabled, a single-token query that is one edit away from a keyword of five or more
    /// characters contributes a fuzzy signal. Ties still fall back to <c>Ambiguous</c>.
    /// </summary>
    public bool EnableFuzzy { get; set; }

    /// <summary>A candidate below this score is not a suggestion at all.</summary>
    public double MinimumScore { get; set; } = 0.65;

    /// <summary>The top two candidates closer than this are ambiguous, never a silent guess.</summary>
    public double AmbiguityMargin { get; set; } = 0.15;

    /// <summary>Fuzzy comparison ignores tokens shorter than this, where one edit dominates.</summary>
    public int FuzzyMinimumTokenLength { get; set; } = 5;

    /// <summary>At most this many query tokens may contribute a fuzzy signal.</summary>
    public int FuzzyMaxTokens { get; set; } = 1;
}
