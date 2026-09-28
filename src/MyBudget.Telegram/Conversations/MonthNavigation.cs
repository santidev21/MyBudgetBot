using System.Globalization;
using MyBudget.Application.Localization;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The <c>[← previous] [current] [next →]</c> row every month-based report shares.
/// <para>
/// The edge buttons disappear at the calendar limits the domain allows, so a tap can never ask
/// for a month outside <see cref="MonthPeriod.MinYear"/>..<see cref="MonthPeriod.MaxYear"/>.
/// </para>
/// </summary>
internal static class MonthNavigation
{
    public static IReadOnlyList<BotButton> Build(
        IUserMessages messages,
        string language,
        MonthPeriod period,
        string previousCallback,
        string currentCallback,
        string nextCallback)
    {
        var buttons = new List<BotButton>();

        if (Previous(period) is { } previous)
        {
            buttons.Add(new BotButton(
                messages.Get(language, MessageKeys.NavigationPrevious, MonthName(messages, language, previous)),
                previousCallback));
        }

        buttons.Add(new BotButton(
            messages.Get(
                language,
                MessageKeys.NavigationCurrent,
                MonthName(messages, language, period),
                period.Year.ToString(CultureInfo.InvariantCulture)),
            currentCallback));

        if (Next(period) is { } next)
        {
            buttons.Add(new BotButton(
                messages.Get(language, MessageKeys.NavigationNext, MonthName(messages, language, next)),
                nextCallback));
        }

        return buttons;
    }

    public static MonthPeriod? Previous(MonthPeriod period) =>
        period.Year == MonthPeriod.MinYear && period.Month == 1 ? null : period.Previous;

    public static MonthPeriod? Next(MonthPeriod period) =>
        period.Year == MonthPeriod.MaxYear && period.Month == 12 ? null : period.Next;

    private static string MonthName(IUserMessages messages, string language, MonthPeriod period) =>
        messages.Get(language, MessageKeys.Months[period.Month - 1]);
}
