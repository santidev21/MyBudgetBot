using MyBudget.Application.Localization;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The persistent main menu, and the mapping from a tapped label back to an action.
/// <para>
/// A reply keyboard sends its label back as ordinary text, so the mapping has to be looked up
/// per language from the same catalog that produced the labels. Comparing against hardcoded
/// Spanish would break the moment a second language is added.
/// </para>
/// </summary>
internal sealed class MainMenu(IUserMessages messages)
{
    public static IReadOnlyList<string> ActionKeys { get; } =
    [
        MessageKeys.MenuAddExpense,
        MessageKeys.MenuSummary,
        MessageKeys.MenuExpenses,
        MessageKeys.MenuCategories,
        MessageKeys.MenuStatistics,
        MessageKeys.MenuSettings,
    ];

    public BotKeyboard ReplyKeyboard(string language) => BotKeyboard.Reply(
        [
            messages.Get(language, MessageKeys.MenuAddExpense),
            messages.Get(language, MessageKeys.MenuSummary),
        ],
        [
            messages.Get(language, MessageKeys.MenuExpenses),
            messages.Get(language, MessageKeys.MenuCategories),
        ],
        [
            messages.Get(language, MessageKeys.MenuStatistics),
            messages.Get(language, MessageKeys.MenuSettings),
        ]);

    /// <summary>Returns the message key of the menu item this text matches, or <c>null</c>.</summary>
    public string? MatchAction(string language, string text)
    {
        foreach (var key in ActionKeys)
        {
            if (string.Equals(messages.Get(language, key), text.Trim(), StringComparison.Ordinal))
            {
                return key;
            }
        }

        return null;
    }
}
