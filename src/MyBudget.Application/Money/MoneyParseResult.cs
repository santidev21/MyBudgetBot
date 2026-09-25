namespace MyBudget.Application.Money;

/// <summary>
/// How the user wrote the amount. Recorded so the interface can decide how much to
/// emphasise the normalised value it is about to save.
/// </summary>
public enum MoneyInputForm
{
    /// <summary>Digits only, for example <c>35000</c>.</summary>
    Plain,

    /// <summary>The currency's own grouping separator, for example <c>35.000</c> for COP.</summary>
    StandardGrouping,

    /// <summary>The other separator used as grouping, for example <c>35,000</c>.</summary>
    ToleratedGrouping,

    /// <summary>Groups separated by spaces, for example <c>35 000</c>.</summary>
    SpaceGrouped,

    /// <summary>A magnitude suffix was applied, for example <c>35k</c> or <c>1,5 millones</c>.</summary>
    SuffixScaled,
}

/// <summary>Why an input could not be turned into an amount.</summary>
public enum MoneyParseError
{
    Empty,
    NotANumber,
    Negative,
    NonPositive,
    FractionNotAllowed,
    TooLarge,
    MalformedGrouping,
}

/// <summary>Why an input has more than one defensible reading.</summary>
public enum MoneyAmbiguityReason
{
    /// <summary>
    /// A single separator followed by exactly three digits, in a currency that has decimal
    /// places: it can be a thousands group or a fraction.
    /// <para>
    /// Unreachable for COP, which has no decimal places, so `3.500` is always 3500 and the
    /// user is never interrupted for it. It exists because the architecture is designed to
    /// take a second currency, and a 2-decimal currency genuinely has this ambiguity.
    /// </para>
    /// </summary>
    SeparatorRoleUnclear,
}

/// <summary>
/// The outcome of parsing user text into an amount. Parsing never throws and never guesses.
/// </summary>
public abstract record MoneyParseResult
{
    private MoneyParseResult()
    {
    }

    /// <param name="Amount">Minor units of the currency: whole pesos for COP.</param>
    /// <param name="Display">The normalised, user-facing representation.</param>
    public sealed record Success(long Amount, string Display, MoneyInputForm Form) : MoneyParseResult;

    /// <param name="Candidates">Every reading that is valid for the currency, at least two.</param>
    public sealed record Ambiguous(
        string Input, IReadOnlyList<long> Candidates, MoneyAmbiguityReason Reason) : MoneyParseResult;

    public sealed record Invalid(string Input, MoneyParseError Reason) : MoneyParseResult;
}
