namespace MyBudget.Infrastructure.Configuration;

/// <summary>
/// A deliberately small <c>.env</c> reader.
/// <para>
/// Docker Compose reads this file for variable substitution; .NET does not. Without this,
/// <c>dotnet run</c> and <c>dotnet ef</c> would need the configuration passed again on every
/// command line, and the two ways of running the service would drift apart.
/// </para>
/// <para>
/// Supported: blank lines, <c>#</c> comments, <c>KEY=value</c>, and single or double quoted
/// values. Not supported on purpose: multi-line values, variable expansion, and <c>export</c>.
/// Anything more belongs in a real configuration provider.
/// </para>
/// </summary>
public static class DotEnvFile
{
    public const string FileName = ".env";

    private const int MaxLevelsUp = 6;

    /// <summary>
    /// Finds the nearest <c>.env</c> walking up from the given directory, the assembly
    /// location, and the current directory. Returns <c>null</c> when there is none.
    /// </summary>
    public static string? Find(string? startDirectory = null)
    {
        var candidates = new[] { startDirectory, AppContext.BaseDirectory, Directory.GetCurrentDirectory() };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var found = WalkUp(candidate);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    public static IReadOnlyDictionary<string, string> Parse(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] == '#')
            {
                continue;
            }

            var separator = trimmed.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0)
            {
                continue;
            }

            var key = trimmed[..separator].Trim();
            if (key.Length == 0)
            {
                continue;
            }

            values[key] = Unquote(trimmed[(separator + 1)..].Trim());
        }

        return values;
    }

    public static IReadOnlyDictionary<string, string> Load(string? startDirectory = null)
    {
        var path = Find(startDirectory);

        return path is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : Parse(File.ReadAllLines(path));
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        var first = value[0];
        var last = value[^1];

        return first == last && first is '"' or '\''
            ? value[1..^1]
            : value;
    }

    private static string? WalkUp(string startDirectory)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory));

        for (var level = 0; level <= MaxLevelsUp && directory is not null; level++)
        {
            var candidate = Path.Combine(directory.FullName, FileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
