using MyBudget.Application.Text;
using MyBudget.Domain.Expenses;

namespace MyBudget.Application.Money;

public enum CompactExpenseOutcome
{
    /// <summary>An amount was found and there is text left to use as a description.</summary>
    Parsed,

    /// <summary>The whole message was just an amount, so it answers "how much?".</summary>
    AmountOnly,

    /// <summary>Two or more amounts are equally plausible and the user must choose.</summary>
    Ambiguous,

    /// <summary>No amount found: this is not a compact expense entry.</summary>
    Unparsed,
}

public sealed record CompactExpenseResult(
    CompactExpenseOutcome Outcome,
    long? Amount,
    string? Description,
    IReadOnlyList<long> Candidates)
{
    public static CompactExpenseResult Parsed(long amount, string? description) =>
        new(CompactExpenseOutcome.Parsed, amount, description, []);

    public static CompactExpenseResult AmountOnly(long amount) =>
        new(CompactExpenseOutcome.AmountOnly, amount, null, []);

    public static CompactExpenseResult Ambiguous(IReadOnlyList<long> candidates) =>
        new(CompactExpenseOutcome.Ambiguous, null, null, candidates);

    public static CompactExpenseResult Unparsed() =>
        new(CompactExpenseOutcome.Unparsed, null, null, []);
}

public interface ICompactExpenseParser
{
    CompactExpenseResult Parse(string? input, string currencyCode);

    CompactExpenseResult Parse(string? input, CurrencyDefinition currency);
}

/// <summary>
/// Extracts an amount and a description from a single free-text message, so recording an
/// expense costs one message instead of three.
/// <para>
/// The selection rules are deterministic and deliberately conservative: when two numbers
/// are equally plausible as the amount, the result is <see cref="CompactExpenseOutcome.Ambiguous"/>
/// and the guided flow asks, rather than silently picking one.
/// </para>
/// </summary>
public sealed class CompactExpenseParser(ICurrencyRegistry currencies, IMoneyParser moneyParser)
    : ICompactExpenseParser
{
    /// <summary>
    /// Words that carry no meaning for the category and only add noise to the description.
    /// Compared after accent folding, so "pagué" and "pague" are the same filler.
    /// </summary>
    private static readonly HashSet<string> LeadingFillers = new(StringComparer.Ordinal)
    {
        "pague", "compre", "gaste", "pago", "compro", "gasto", "page",
        "de", "del", "en", "por", "para", "con",
        "un", "una", "unos", "unas", "el", "la", "los", "las", "mi", "mis",
    };

    public CompactExpenseResult Parse(string? input, string currencyCode) =>
        Parse(input, currencies.Get(currencyCode));

    public CompactExpenseResult Parse(string? input, CurrencyDefinition currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        if (string.IsNullOrWhiteSpace(input))
        {
            return CompactExpenseResult.Unparsed();
        }

        var text = input.Trim();

        // The whole message is an amount: it answers the "how much?" prompt.
        if (moneyParser.Parse(text, currency) is MoneyParseResult.Success onlyAmount)
        {
            return CompactExpenseResult.AmountOnly(onlyAmount.Amount);
        }

        var tokens = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var spans = FindAmountSpans(tokens, currency);

        if (spans.Count == 0)
        {
            return CompactExpenseResult.Unparsed();
        }

        var chosen = ChooseSpan(spans);

        if (chosen is null)
        {
            return CompactExpenseResult.Ambiguous(
                spans.Select(span => span.Amount).Distinct().ToList());
        }

        return CompactExpenseResult.Parsed(
            chosen.Value.Amount, BuildDescription(tokens, chosen.Value));
    }

    private List<AmountSpan> FindAmountSpans(IReadOnlyList<string> tokens, CurrencyDefinition currency)
    {
        var spans = new List<AmountSpan>();
        var current = new List<string>();
        var start = 0;

        for (var index = 0; index < tokens.Count; index++)
        {
            if (!IsAmountToken(tokens[index], currency))
            {
                Flush();
                continue;
            }

            if (current.Count == 0)
            {
                start = index;
            }

            current.Add(tokens[index]);
        }

        Flush();
        return spans;

        void Flush()
        {
            if (current.Count == 0)
            {
                return;
            }

            // Adjacent numeric tokens are one span, so "35 000" is a single amount rather
            // than 35 followed by 000.
            var text = string.Join(' ', current);

            if (moneyParser.Parse(text, currency) is MoneyParseResult.Success success)
            {
                spans.Add(new AmountSpan(start, current.Count, success.Amount, HasMoneyMarker(current, currency)));
            }

            current.Clear();
        }
    }

    private static bool IsAmountToken(string token, CurrencyDefinition currency)
    {
        if (token.Any(char.IsAsciiDigit))
        {
            return true;
        }

        var folded = TextNormalizer.Fold(token);

        return currency.MagnitudeSuffixes.Any(suffix => suffix.Token == folded)
               || token == currency.Symbol
               || token == "$";
    }

    private static bool HasMoneyMarker(IReadOnlyList<string> tokens, CurrencyDefinition currency)
    {
        if (tokens.Count > 1)
        {
            // A multi-token span is either a suffix or spaced grouping: both are deliberate.
            return true;
        }

        var token = tokens[0];

        return token.Contains('.', StringComparison.Ordinal)
               || token.Contains(',', StringComparison.Ordinal)
               || token == currency.Symbol
               || token == "$";
    }

    private static AmountSpan? ChooseSpan(IReadOnlyList<AmountSpan> spans)
    {
        if (spans.Count == 1)
        {
            return spans[0];
        }

        // Exactly one marked amount ("2 x 3.500") is unambiguous even though 2 is a number.
        var marked = spans.Where(span => span.HasMarker).ToList();
        if (marked.Count == 1)
        {
            return marked[0];
        }

        var candidates = (marked.Count > 1 ? marked : spans.ToList())
            .OrderByDescending(span => span.Amount)
            .ToList();

        // "2 x 3500" is an amount plus a multiplier. "12 y 15" is genuinely two amounts, and
        // picking the larger one there would be a silent guess.
        return candidates[0].Amount >= candidates[1].Amount * 10
            ? candidates[0]
            : null;
    }

    private static string? BuildDescription(IReadOnlyList<string> tokens, AmountSpan span)
    {
        var dropped = new bool[tokens.Count];

        for (var index = span.Start; index < span.Start + span.Length; index++)
        {
            dropped[index] = true;
        }

        // "2 x 3.500" is a count and a multiplier around the amount, not a description.
        for (var index = 1; index < tokens.Count; index++)
        {
            if (dropped[index] || !IsMultiplicationMarker(tokens[index]) || dropped[index - 1])
            {
                continue;
            }

            if (tokens[index - 1].Any(char.IsAsciiDigit))
            {
                dropped[index - 1] = true;
                dropped[index] = true;
            }
        }

        var remaining = new List<string>(tokens.Count);
        for (var index = 0; index < tokens.Count; index++)
        {
            if (!dropped[index])
            {
                remaining.Add(tokens[index]);
            }
        }

        // Keep the last token even if it looks like filler, so a description is never lost.
        var first = 0;
        while (first < remaining.Count - 1 && IsFiller(remaining[first]))
        {
            first++;
        }

        var description = string.Join(' ', remaining.Skip(first))
            .Trim()
            .Trim('.', ',', ';', ':', '!', '?', '-');

        if (description.Length == 0)
        {
            return null;
        }

        return description.Length <= Expense.MaxDescriptionLength
            ? description
            : description[..Expense.MaxDescriptionLength].TrimEnd();
    }

    private static bool IsMultiplicationMarker(string token) =>
        token is "*" or "×" || TextNormalizer.Fold(token) is "x" or "por";

    private static bool IsFiller(string token) => LeadingFillers.Contains(TextNormalizer.Fold(token));

    private readonly record struct AmountSpan(int Start, int Length, long Amount, bool HasMarker);
}
