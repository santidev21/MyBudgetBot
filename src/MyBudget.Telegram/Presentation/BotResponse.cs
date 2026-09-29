namespace MyBudget.Telegram.Presentation;

/// <summary>
/// One message the bot wants to send.
/// <para>
/// Deliberately free of Telegram types: conversations produce these, and a presenter turns
/// them into API calls. That is what makes a conversation testable without the SDK, and what
/// keeps every user-visible string on one path where it can be escaped.
/// </para>
/// </summary>
public sealed class BotResponse
{
    private BotResponse(string text, BotKeyboard? keyboard, byte[]? photo)
    {
        Text = text;
        Keyboard = keyboard;
        Photo = photo;
    }

    public string Text { get; }

    public BotKeyboard? Keyboard { get; }

    /// <summary>
    /// A PNG to send as a photo, or <c>null</c> for a plain message. When set,
    /// <see cref="Text"/> is the caption.
    /// </summary>
    public byte[]? Photo { get; }

    public static BotResponse Message(string text) => new(text, null, null);

    public static BotResponse Message(string text, BotKeyboard keyboard) => new(text, keyboard, null);

    public static BotResponse WithPhoto(byte[] png, string caption, BotKeyboard? keyboard = null) =>
        new(caption, keyboard, png);
}
