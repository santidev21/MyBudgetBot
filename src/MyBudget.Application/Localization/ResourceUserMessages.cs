using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Options;
using MyBudget.Application.Configuration;

namespace MyBudget.Application.Localization;

/// <summary>
/// Resolves messages from embedded <c>.resx</c> resources.
/// <para>
/// The neutral resource set is Spanish, declared with <c>NeutralResourcesLanguage</c> in the
/// project file, so the default language needs no satellite assembly and unknown languages
/// fall back to Spanish rather than to nothing. Adding a language means adding
/// <c>Resources/Messages.&lt;culture&gt;.resx</c> and listing the culture in
/// <c>Localization:SupportedLanguages</c>.
/// </para>
/// </summary>
public sealed class ResourceUserMessages : IUserMessages
{
    private const string ResourceBaseName = "MyBudget.Application.Resources.Messages";

    private static readonly ResourceManager Resources = new(ResourceBaseName, typeof(ResourceUserMessages).Assembly);

    private readonly LocalizationOptions _options;

    public ResourceUserMessages(IOptions<LocalizationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public IReadOnlyCollection<string> SupportedLanguages => _options.SupportedLanguages;

    public bool Contains(string language, string key) => Find(language, key) is not null;

    public string Get(string language, string key, params object?[] args)
    {
        var found = Find(language, key);

        if (found is null)
        {
            // Never render an empty message: the key is at least diagnosable.
            return key;
        }

        var (text, culture) = found.Value;
        return args.Length == 0 ? text : string.Format(culture, text, args);
    }

    private (string Text, CultureInfo Culture)? Find(string language, string key)
    {
        foreach (var candidate in new[] { language, _options.DefaultLanguage })
        {
            var culture = TryResolveCulture(candidate);
            if (culture is null)
            {
                continue;
            }

            if (Resources.GetString(key, culture) is { Length: > 0 } text)
            {
                return (text, culture);
            }
        }

        return null;
    }

    private static CultureInfo? TryResolveCulture(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        try
        {
            return CultureInfo.GetCultureInfo(language);
        }
        catch (ArgumentException)
        {
            // CultureNotFoundException derives from ArgumentException; both mean "this tag
            // means nothing to this machine", and neither may reach the user as an error.
            return null;
        }
    }
}
