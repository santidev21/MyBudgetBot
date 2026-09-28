namespace MyBudget.Application.Matching;

/// <summary>
/// Deterministic category matching (design §9). No AI, no randomness, no I/O.
/// <para>
/// It scores the user's category names and keywords against a free-text description and decides
/// between <c>Matched</c>, <c>Ambiguous</c> and <c>None</c>. The caller decides what to do with
/// the answer; the matcher only measures.
/// </para>
/// </summary>
public interface ICategoryMatcher
{
    CategoryMatchResult Match(string query, IReadOnlyList<CategoryMatchInput> categories);
}
