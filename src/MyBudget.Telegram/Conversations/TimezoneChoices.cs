namespace MyBudget.Telegram.Conversations;

/// <summary>
/// A short list of time zones rather than the full IANA catalog.
/// <para>
/// The bot serves Colombian users first, and a 400-entry picker inside a chat is unusable, so
/// the common cases are buttons and anything else is typed by hand.
/// </para>
/// </summary>
internal static class TimezoneChoices
{
    public const string CustomCallbackData = "tz:custom";

    public static IReadOnlyList<(string Label, string TimeZoneId)> Common { get; } =
    [
        ("🇨🇴 Bogotá", "America/Bogota"),
        ("🇲🇽 Ciudad de México", "America/Mexico_City"),
        ("🇦🇷 Buenos Aires", "America/Argentina/Buenos_Aires"),
        ("🇵🇪 Lima", "America/Lima"),
        ("🇨🇱 Santiago", "America/Santiago"),
        ("🇪🇸 Madrid", "Europe/Madrid"),
    ];
}
