using Microsoft.Extensions.Logging;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Recurring;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Recurring;

/// <summary>
/// Tells each user which recurring expenses the pass just registered.
/// <para>
/// The expenses are already committed by the time this runs, so a delivery failure is logged
/// and never propagated: losing the notification must not lose the expense.
/// </para>
/// </summary>
internal sealed class RecurringExpenseNotifier(
    ITelegramSender sender,
    IUserMessages messages,
    IMoneyFormatter moneyFormatter,
    ILogger<RecurringExpenseNotifier> logger)
{
    public async Task NotifyAsync(
        IReadOnlyList<RecurringApplicationResult> results, CancellationToken cancellationToken = default)
    {
        foreach (var result in results)
        {
            var language = result.User.Language;
            var lines = new List<string> { messages.Get(language, MessageKeys.RecurringAppliedHeader) };

            foreach (var generated in result.Generated)
            {
                var label = string.IsNullOrWhiteSpace(generated.Description)
                    ? $"{generated.Icon} {generated.CategoryName}"
                    : $"{generated.Icon} {generated.Description}";

                lines.Add(messages.Get(
                    language,
                    MessageKeys.RecurringAppliedLine,
                    DateLabel(language, generated.Date),
                    label,
                    moneyFormatter.Format(generated.Amount, result.User.Currency)));
            }

            try
            {
                await sender.SendAsync(
                    result.User.TelegramUserId,
                    [BotResponse.Message(string.Join("\n", lines))],
                    cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "RecurringExpenseNotificationFailed {UserId}", result.User.Id);
            }
        }
    }

    private string DateLabel(string language, DateOnly date) =>
        $"{date.Day} de {messages.Get(language, MessageKeys.Months[date.Month - 1])} de {date.Year}";
}
