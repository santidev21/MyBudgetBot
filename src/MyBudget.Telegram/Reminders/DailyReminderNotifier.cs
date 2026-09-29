using Microsoft.Extensions.Logging;
using MyBudget.Application.Localization;
using MyBudget.Application.Reminders;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Reminders;

/// <summary>
/// Sends the evening reminder to each due user.
/// <para>
/// The marker is already claimed by the time this runs, so a delivery failure is logged and never
/// propagated: losing the message must not take the service down, and the marker is what keeps
/// the bot from repeating itself.
/// </para>
/// </summary>
internal sealed class DailyReminderNotifier(
    ITelegramSender sender,
    IUserMessages messages,
    ILogger<DailyReminderNotifier> logger)
{
    public async Task NotifyAsync(
        IReadOnlyList<DailyReminder> reminders, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reminders);

        foreach (var reminder in reminders)
        {
            try
            {
                await sender.SendAsync(
                    reminder.User.TelegramUserId,
                    [BotResponse.Message(
                        messages.Get(reminder.User.Language, MessageKeys.DailyReminderMessage))],
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception, "DailyReminderNotificationFailed {UserId}", reminder.User.Id);
            }
        }
    }
}
