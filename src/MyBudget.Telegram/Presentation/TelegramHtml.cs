namespace MyBudget.Telegram.Presentation;

/// <summary>
/// Escaping for Telegram's HTML parse mode.
/// <para>
/// Every message the bot sends goes through here, including text the user typed. That is what
/// stops a description such as <c>pago &lt;b&gt;raro&lt;/b&gt;</c> from changing the structure
/// of a message, or from breaking it entirely.
/// </para>
/// </summary>
public static class TelegramHtml
{
    public static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal);
}
