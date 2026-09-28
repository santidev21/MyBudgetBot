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
    private BotResponse(string text, BotKeyboard? keyboard)
    {
        Text = text;
        Keyboard = keyboard;
    }

    public string Text { get; }

    public BotKeyboard? Keyboard { get; }

    public static BotResponse Message(string text) => new(text, null);

    public static BotResponse Message(string text, BotKeyboard keyboard) => new(text, keyboard);
}
