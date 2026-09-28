using Telegram.Bot.Types;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// Command handling.
/// <para>
/// Command identifiers are English like every other identifier; only the descriptions shown in
/// Telegram's command menu are Spanish.
/// </para>
/// </summary>
internal static class BotCommands
{
    public const string Start = "start";
    public const string Help = "help";
    public const string Cancel = "cancel";

    /// <summary>
    /// Extracts the command name, tolerating a bot suffix (<c>/start@MyBudgetBot</c>) and
    /// arguments. Returns <c>null</c> when the text is not a command.
    /// </summary>
    public static string? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text[0] != '/')
        {
            return null;
        }

        var token = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries)[0];
        var at = token.IndexOf('@', StringComparison.Ordinal);

        if (at >= 0)
        {
            token = token[..at];
        }

        return token[1..].ToLowerInvariant();
    }

    /// <summary>The command menu Telegram shows next to the text field.</summary>
    public static BotCommand[] Catalog(string startDescription, string helpDescription, string cancelDescription) =>
    [
        new(Start, startDescription),
        new(Help, helpDescription),
        new(Cancel, cancelDescription),
    ];
}
