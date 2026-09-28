using Microsoft.Extensions.Options;

namespace MyBudget.Application.Matching;

/// <inheritdoc />
/// <remarks>
/// Signals are additive and capped at 1.0. For one query/keyword pair the pair score is the best
/// of { phrase, full coverage } or the partial-overlap term, plus the fuzzy signal when it applies
/// and the small bonus for a category-name match. A category takes the best score across its name
/// and keywords. Selection then compares the two best categories:
/// <code>
/// top &lt; MinimumScore            -> None
/// top - second &lt; AmbiguityMargin -> Ambiguous
/// otherwise                        -> Matched
/// </code>
/// The partial-overlap term keeps its documented 0.20 floor, which is what lets a lone fuzzy match
/// (0.20 + 0.45) reach the 0.65 threshold. Scores are compared with a small epsilon because the
/// threshold is the sum of two binary fractions.
/// </remarks>
public sealed class CategoryMatcher : ICategoryMatcher
{
    private const double PhraseBase = 0.85;
    private const double PhraseCoverageBonus = 0.05;
    private const double PhraseCap = 0.95;
    private const double FullCoverageScore = 0.80;
    private const double PartialBase = 0.20;
    private const double PartialRatioWeight = 0.50;
    private const double FuzzyScore = 0.45;
    private const double NameBonus = 0.05;
    private const double ScoreEpsilon = 1e-9;

    private readonly CategoryMatchingOptions _options;

    public CategoryMatcher(IOptions<CategoryMatchingOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public CategoryMatchResult Match(string query, IReadOnlyList<CategoryMatchInput> categories)
    {
        ArgumentNullException.ThrowIfNull(categories);

        var normalizedQuery = MatchText.Normalize(query);
        if (normalizedQuery.Length == 0)
        {
            return CategoryMatchResult.NoMatch();
        }

        var queryTokens = MatchText.Tokenize(normalizedQuery);

        var ranked = categories
            .Select(category => new CategoryMatchCandidate(
                category.CategoryId,
                category.Name,
                category.Icon,
                ScoreCategory(normalizedQuery, queryTokens, category)))
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal)
            .ToList();

        if (ranked.Count == 0)
        {
            return CategoryMatchResult.NoMatch();
        }

        var top = ranked[0];
        if (top.Score + ScoreEpsilon < _options.MinimumScore)
        {
            return CategoryMatchResult.NoMatch();
        }

        var second = ranked.Count > 1 ? ranked[1].Score : 0;

        if (top.Score - second + ScoreEpsilon < _options.AmbiguityMargin)
        {
            var tied = ranked
                .Where(candidate => candidate.Score + ScoreEpsilon >= _options.MinimumScore)
                .ToList();

            return new CategoryMatchResult(CategoryMatchOutcome.Ambiguous, null, tied);
        }

        return new CategoryMatchResult(CategoryMatchOutcome.Matched, top, [top]);
    }

    /// <summary>
    /// The score the matcher would give one category, without the selection rule.
    /// <para>
    /// Internal so the signal table can be pinned precisely, where a selection outcome would hide
    /// the small differences between signals.
    /// </para>
    /// </summary>
    internal double Score(string query, CategoryMatchInput category)
    {
        ArgumentNullException.ThrowIfNull(category);

        var normalizedQuery = MatchText.Normalize(query);
        return normalizedQuery.Length == 0
            ? 0.0
            : ScoreCategory(normalizedQuery, MatchText.Tokenize(normalizedQuery), category);
    }

    private double ScoreCategory(
        string normalizedQuery, string[] queryTokens, CategoryMatchInput category)
    {
        var best = 0.0;

        foreach (var alias in category.Aliases)
        {
            var score = ScoreTerm(normalizedQuery, queryTokens, alias, isCategoryName: false);
            best = Math.Max(best, score);
        }

        var nameScore = ScoreTerm(normalizedQuery, queryTokens, category.Name, isCategoryName: true);
        return Math.Min(Math.Max(best, nameScore), 1.0);
    }

    private double ScoreTerm(
        string normalizedQuery, string[] queryTokens, string? term, bool isCategoryName)
    {
        var normalizedTerm = MatchText.Normalize(term);
        if (normalizedTerm.Length == 0)
        {
            return 0.0;
        }

        var termTokens = MatchText.Tokenize(normalizedTerm);

        var score = normalizedQuery == normalizedTerm
            ? 1.0
            : BestStructuralScore(queryTokens, termTokens);

        if (_options.EnableFuzzy && FuzzyApplies(queryTokens, termTokens))
        {
            score += FuzzyScore;
        }

        if (isCategoryName)
        {
            score += NameBonus;
        }

        return Math.Min(score, 1.0);
    }

    private static double BestStructuralScore(string[] queryTokens, string[] termTokens)
    {
        // The documented 0.20 floor applies to any pair, so a fuzzy-only near miss can reach the
        // selection threshold. It is comfortably below every threshold on its own.
        var score = PartialOverlap(queryTokens, termTokens);

        var phrase = PhraseContainmentScore(queryTokens, termTokens);
        if (phrase > score)
        {
            score = phrase;
        }

        if (queryTokens.Length >= 2 && FullTokenCoverage(queryTokens, termTokens))
        {
            score = Math.Max(score, FullCoverageScore);
        }

        return score;
    }

    private static double PartialOverlap(string[] queryTokens, string[] termTokens)
    {
        if (queryTokens.Length == 0 || termTokens.Length == 0)
        {
            return PartialBase;
        }

        var matched = CountOverlappingTokens(queryTokens, termTokens);
        var ratio = (double)matched / Math.Max(queryTokens.Length, termTokens.Length);
        return (PartialRatioWeight * ratio) + PartialBase;
    }

    private static int CountOverlappingTokens(string[] queryTokens, string[] termTokens)
    {
        var used = new bool[termTokens.Length];
        var matched = 0;

        foreach (var queryToken in queryTokens)
        {
            for (var index = 0; index < termTokens.Length; index++)
            {
                if (!used[index] && MatchText.TokensMatch(queryToken, termTokens[index]))
                {
                    used[index] = true;
                    matched++;
                    break;
                }
            }
        }

        return matched;
    }

    private static double PhraseContainmentScore(string[] queryTokens, string[] termTokens)
    {
        if (queryTokens.Length == 0 || termTokens.Length == 0)
        {
            return 0;
        }

        var shorter = queryTokens.Length <= termTokens.Length ? queryTokens : termTokens;
        var longer = queryTokens.Length <= termTokens.Length ? termTokens : queryTokens;

        if (shorter.Length == longer.Length || !AppearsContiguously(shorter, longer))
        {
            return 0;
        }

        var coverage = (double)shorter.Length / longer.Length;
        return Math.Min(PhraseBase + (PhraseCoverageBonus * coverage), PhraseCap);
    }

    private static bool AppearsContiguously(string[] shorter, string[] longer)
    {
        for (var start = 0; start + shorter.Length <= longer.Length; start++)
        {
            var all = true;
            for (var offset = 0; offset < shorter.Length; offset++)
            {
                if (!MatchText.TokensMatch(shorter[offset], longer[start + offset]))
                {
                    all = false;
                    break;
                }
            }

            if (all)
            {
                return true;
            }
        }

        return false;
    }

    private static bool FullTokenCoverage(string[] queryTokens, string[] termTokens)
    {
        var shorter = queryTokens.Length <= termTokens.Length ? queryTokens : termTokens;
        var longer = queryTokens.Length <= termTokens.Length ? termTokens : queryTokens;

        return shorter.Length >= 2 && CountOverlappingTokens(shorter, longer) == shorter.Length;
    }

    private bool FuzzyApplies(string[] queryTokens, string[] termTokens)
    {
        if (queryTokens.Length == 0 || queryTokens.Length > _options.FuzzyMaxTokens)
        {
            return false;
        }

        foreach (var queryToken in queryTokens)
        {
            if (queryToken.Length < _options.FuzzyMinimumTokenLength)
            {
                return false;
            }

            if (!HasCloseToken(queryToken, termTokens))
            {
                return false;
            }
        }

        return true;
    }

    private bool HasCloseToken(string queryToken, string[] termTokens)
    {
        foreach (var termToken in termTokens)
        {
            if (termToken.Length < _options.FuzzyMinimumTokenLength
                || string.Equals(queryToken, termToken, StringComparison.Ordinal))
            {
                continue;
            }

            if (IsOneEditAway(queryToken, termToken))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Levenshtein distance of exactly one: one insertion, deletion or substitution.</summary>
    private static bool IsOneEditAway(string left, string right)
    {
        if (Math.Abs(left.Length - right.Length) > 1)
        {
            return false;
        }

        if (left.Length == right.Length)
        {
            var differences = 0;
            for (var index = 0; index < left.Length; index++)
            {
                if (left[index] != right[index] && ++differences > 1)
                {
                    return false;
                }
            }

            return differences == 1;
        }

        var shorter = left.Length < right.Length ? left : right;
        var longer = left.Length < right.Length ? right : left;

        var shortIndex = 0;
        var longIndex = 0;
        var skipped = false;

        while (shortIndex < shorter.Length && longIndex < longer.Length)
        {
            if (shorter[shortIndex] == longer[longIndex])
            {
                shortIndex++;
                longIndex++;
                continue;
            }

            if (skipped)
            {
                return false;
            }

            skipped = true;
            longIndex++;
        }

        return true;
    }
}
