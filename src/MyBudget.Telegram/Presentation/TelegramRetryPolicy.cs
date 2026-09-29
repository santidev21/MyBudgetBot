namespace MyBudget.Telegram.Presentation;

/// <summary>
/// How outbound sends react to Telegram's <c>429 Too Many Requests</c>.
/// <para>
/// Kept as pure arithmetic so it can be tested without waiting: the sender only supplies the
/// number Telegram returned, and this decides how long to pause. The delay is capped because
/// Telegram reports <c>retry_after</c> in seconds but a hostile or buggy value in the millions
/// would otherwise park the update pipeline; after the attempts are spent the failure
/// propagates and Telegram redelivers the webhook later.
/// </para>
/// </summary>
internal static class TelegramRetryPolicy
{
    public const int MaxAttempts = 3;

    private const int DefaultRetryAfterSeconds = 1;
    private const int MaxRetryAfterSeconds = 60;

    public static TimeSpan DelayFor(int? retryAfterSeconds)
    {
        var seconds = retryAfterSeconds is > 0 ? retryAfterSeconds.Value : DefaultRetryAfterSeconds;
        return TimeSpan.FromSeconds(Math.Min(seconds, MaxRetryAfterSeconds));
    }
}
