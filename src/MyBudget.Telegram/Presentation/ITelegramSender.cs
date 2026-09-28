namespace MyBudget.Telegram.Presentation;

public interface ITelegramSender
{
    /// <summary>
    /// Sends the replies of one turn, in order. Throws when Telegram cannot be reached, so the
    /// caller can decide whether the update should be retried.
    /// </summary>
    Task SendAsync(long chatId, IReadOnlyList<BotResponse> responses, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the loading spinner on an inline button. A failure here is cosmetic and must never
    /// fail the update.
    /// </summary>
    Task AcknowledgeCallbackAsync(
        string callbackQueryId, string? notification = null, CancellationToken cancellationToken = default);
}
