namespace MyBudget.Telegram.Presentation;

public enum BotKeyboardKind
{
    /// <summary>A persistent menu: the button's label comes back as a normal message.</summary>
    Reply,

    /// <summary>Contextual buttons that carry a payload instead of a label.</summary>
    Inline,
}

public sealed record BotButton(string Text, string? CallbackData = null);

/// <summary>
/// A keyboard, described in terms the domain of the bot understands rather than in Telegram
/// types, so conversations can be tested without the SDK.
/// </summary>
public sealed class BotKeyboard
{
    private BotKeyboard(BotKeyboardKind kind, IReadOnlyList<IReadOnlyList<BotButton>> rows)
    {
        Kind = kind;
        Rows = rows;
    }

    public BotKeyboardKind Kind { get; }

    public IReadOnlyList<IReadOnlyList<BotButton>> Rows { get; }

    public static BotKeyboard Reply(params string[][] rows) =>
        new(
            BotKeyboardKind.Reply,
            rows.Select(row => (IReadOnlyList<BotButton>)row.Select(label => new BotButton(label)).ToList())
                .ToList());

    public static BotKeyboard Inline(params BotButton[][] rows) => new(BotKeyboardKind.Inline, rows);
}
