using Microsoft.Extensions.Options;

namespace MyBudget.Application.Matching;

/// <summary>
/// Validates matching policy at startup.
/// <para>
/// These numbers decide whether the bot guesses a category or asks. A score above 1 or a
/// negative margin would make the matcher behave in ways the tests never describe, so a bad
/// deployment fails at boot with a clear message instead of miscategorising an expense.
/// </para>
/// </summary>
public sealed class CategoryMatchingOptionsValidator : IValidateOptions<CategoryMatchingOptions>
{
    public ValidateOptionsResult Validate(string? name, CategoryMatchingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var section = CategoryMatchingOptions.SectionName;
        var failures = new List<string>();

        if (options.MinimumScore is < 0 or > 1)
        {
            failures.Add($"{section}:MinimumScore must be between 0 and 1.");
        }

        if (options.AmbiguityMargin is < 0 or > 1)
        {
            failures.Add($"{section}:AmbiguityMargin must be between 0 and 1.");
        }

        if (options.FuzzyMinimumTokenLength < 1)
        {
            failures.Add($"{section}:FuzzyMinimumTokenLength must be at least 1.");
        }

        if (options.FuzzyMaxTokens < 1)
        {
            failures.Add($"{section}:FuzzyMaxTokens must be at least 1.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
