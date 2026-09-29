using Microsoft.Extensions.Logging;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reporting;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Reporting;

/// <summary>
/// Sends each due user the closing of the previous month.
/// <para>
/// The marker is already claimed by the time this runs, so a delivery failure is logged and never
/// propagated: losing the message must not take the service down, and the marker is what keeps
/// the bot from repeating itself.
/// </para>
/// </summary>
internal sealed class MonthlyClosingNotifier(
    ITelegramSender sender,
    IUserMessages messages,
    IMoneyFormatter moneyFormatter,
    ILogger<MonthlyClosingNotifier> logger)
{
    public async Task NotifyAsync(
        IReadOnlyList<MonthlyClosing> closings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(closings);

        foreach (var closing in closings)
        {
            try
            {
                await sender.SendAsync(
                    closing.User.TelegramUserId,
                    [MonthlyClosingMessages.Format(messages, moneyFormatter, closing)],
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "MonthlyClosingNotificationFailed {UserId}", closing.User.Id);
            }
        }
    }
}
