namespace MyBudget.Application.Localization;

/// <summary>
/// Identifiers for every user-facing message.
/// <para>
/// Keys are English, like every other identifier in the codebase. Only the resource
/// <em>values</em> are Spanish. A test asserts that this list, the names declared here and
/// the resources on disk all agree, so a missing translation fails the build instead of
/// showing a raw key to a user.
/// </para>
/// </summary>
public static class MessageKeys
{
    public const string Welcome = nameof(Welcome);
    public const string Help = nameof(Help);
    public const string AccessDenied = nameof(AccessDenied);
    public const string Cancelled = nameof(Cancelled);
    public const string ConversationExpired = nameof(ConversationExpired);
    public const string UnexpectedError = nameof(UnexpectedError);

    public const string MenuSummary = "Menu.Summary";
    public const string MenuAddExpense = "Menu.AddExpense";
    public const string MenuExpenses = "Menu.Expenses";
    public const string MenuCategories = "Menu.Categories";
    public const string MenuStatistics = "Menu.Statistics";
    public const string MenuSettings = "Menu.Settings";

    public const string ButtonCancel = "Buttons.Cancel";
    public const string ButtonConfirm = "Buttons.Confirm";
    public const string ButtonEdit = "Buttons.Edit";
    public const string ButtonChangeCategory = "Buttons.ChangeCategory";
    public const string ButtonChangeDate = "Buttons.ChangeDate";
    public const string ButtonUndo = "Buttons.Undo";

    public const string AmountPrompt = "Amount.Prompt";
    public const string AmountInvalid = "Amount.Invalid";
    public const string AmountNegative = "Amount.Negative";
    public const string AmountTooLarge = "Amount.TooLarge";
    public const string AmountFractionNotAllowed = "Amount.FractionNotAllowed";
    public const string AmountAmbiguous = "Amount.Ambiguous";

    public const string DatePrompt = "Date.Prompt";
    public const string DateInvalid = "Date.Invalid";
    public const string DateFuture = "Date.Future";
    public const string DateTooOld = "Date.TooOld";

    public const string ExpenseRegistered = "Expense.Registered";
    public const string ExpenseConfirmationHeader = "Expense.ConfirmationHeader";
    public const string ExpenseCategorySuggestion = "Expense.CategorySuggestion";
    public const string ExpenseCategoryAmbiguous = "Expense.CategoryAmbiguous";
    public const string ExpenseCategoryNone = "Expense.CategoryNone";

    /// <summary>Every key that must exist in every supported language.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Welcome,
        Help,
        AccessDenied,
        Cancelled,
        ConversationExpired,
        UnexpectedError,
        MenuSummary,
        MenuAddExpense,
        MenuExpenses,
        MenuCategories,
        MenuStatistics,
        MenuSettings,
        ButtonCancel,
        ButtonConfirm,
        ButtonEdit,
        ButtonChangeCategory,
        ButtonChangeDate,
        ButtonUndo,
        AmountPrompt,
        AmountInvalid,
        AmountNegative,
        AmountTooLarge,
        AmountFractionNotAllowed,
        AmountAmbiguous,
        DatePrompt,
        DateInvalid,
        DateFuture,
        DateTooOld,
        ExpenseRegistered,
        ExpenseConfirmationHeader,
        ExpenseCategorySuggestion,
        ExpenseCategoryAmbiguous,
        ExpenseCategoryNone,
    ];
}
