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

    /// <summary>Shown once per window when a user sends updates faster than the throttle allows.</summary>
    public const string RateLimited = nameof(RateLimited);

    public const string MenuSummary = "Menu.Summary";
    public const string MenuAddExpense = "Menu.AddExpense";
    public const string MenuExpenses = "Menu.Expenses";
    public const string MenuRecurring = "Menu.Recurring";
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

    public const string OnboardingTimezonePrompt = "Onboarding.TimezonePrompt";
    public const string OnboardingTimezoneSaved = "Onboarding.TimezoneSaved";
    public const string OnboardingTimezoneCustomPrompt = "Onboarding.TimezoneCustomPrompt";
    public const string OnboardingTimezoneCustomInvalid = "Onboarding.TimezoneCustomInvalid";
    public const string FeatureNotReady = "FeatureNotReady";

    public const string CommandStartDescription = "Command.Start.Description";
    public const string CommandHelpDescription = "Command.Help.Description";
    public const string CommandCancelDescription = "Command.Cancel.Description";

    public const string ExpenseRegistered = "Expense.Registered";
    public const string ExpenseConfirmationHeader = "Expense.ConfirmationHeader";
    public const string ExpenseCategorySuggestion = "Expense.CategorySuggestion";
    public const string ExpenseCategoryAmbiguous = "Expense.CategoryAmbiguous";
    public const string ExpenseCategoryNone = "Expense.CategoryNone";
    public const string ExpenseDescriptionPrompt = "Expense.DescriptionPrompt";
    public const string ExpenseCategoryPrompt = "Expense.CategoryPrompt";
    public const string ExpenseNoCategories = "Expense.NoCategories";
    public const string ExpenseConfirmationAmount = "Expense.ConfirmationAmount";
    public const string ExpenseConfirmationDescription = "Expense.ConfirmationDescription";
    public const string ExpenseConfirmationCategory = "Expense.ConfirmationCategory";
    public const string ExpenseConfirmationDate = "Expense.ConfirmationDate";
    public const string ExpenseNoDescription = "Expense.NoDescription";
    public const string ExpenseAmbiguousAmount = "Expense.AmbiguousAmount";
    public const string ExpenseEditPrompt = "Expense.EditPrompt";
    public const string ExpenseButtonAmount = "Expense.ButtonAmount";
    public const string ExpenseButtonDescription = "Expense.ButtonDescription";
    public const string ExpenseButtonCategory = "Expense.ButtonCategory";
    public const string ExpenseUpdated = "Expense.Updated";
    public const string ExpenseExpired = "Expense.Expired";
    public const string ExpenseNotFound = "Expense.NotFound";
    public const string ExpenseUndone = "Expense.Undone";
    public const string ExpenseDeleted = "Expense.Deleted";
    public const string ExpenseDetailHeader = "Expense.DetailHeader";
    public const string ExpenseDeleteConfirm = "Expense.DeleteConfirm";
    public const string ExpenseDeletedConfirm = "Expense.DeletedConfirm";
    public const string ExpenseLearnKeywordPrompt = "Expense.LearnKeywordPrompt";
    public const string ExpenseLearnKeywordSaved = "Expense.LearnKeywordSaved";

    public const string ButtonSkip = "Buttons.Skip";
    public const string ButtonDelete = "Buttons.Delete";
    public const string ButtonToday = "Buttons.Today";
    public const string ButtonYesterday = "Buttons.Yesterday";
    public const string ButtonBack = "Buttons.Back";
    public const string ButtonSaveAlias = "Buttons.SaveAlias";

    public const string CategoryListHeader = "Category.ListHeader";
    public const string CategoryListEmpty = "Category.ListEmpty";
    public const string CategoryButtonNew = "Category.ButtonNew";
    public const string CategoryButtonBudget = "Category.ButtonBudget";
    public const string CategoryButtonBack = "Category.ButtonBack";
    public const string CategoryButtonRename = "Category.ButtonRename";
    public const string CategoryButtonIcon = "Category.ButtonIcon";
    public const string CategoryButtonAliases = "Category.ButtonAliases";
    public const string CategoryButtonDeactivate = "Category.ButtonDeactivate";
    public const string CategoryButtonActivate = "Category.ButtonActivate";
    public const string CategoryButtonSkip = "Category.ButtonSkip";
    public const string CategoryInactiveMarker = "Category.InactiveMarker";
    public const string CategoryStatusInactive = "Category.StatusInactive";
    public const string CategoryDetailHeader = "Category.DetailHeader";
    public const string CategoryNamePrompt = "Category.NamePrompt";
    public const string CategoryNameInvalid = "Category.NameInvalid";
    public const string CategoryNameTaken = "Category.NameTaken";
    public const string CategoryCreated = "Category.Created";
    public const string CategoryReactivated = "Category.Reactivated";
    public const string CategoryRenamePrompt = "Category.RenamePrompt";
    public const string CategoryRenamed = "Category.Renamed";
    public const string CategoryIconPrompt = "Category.IconPrompt";
    public const string CategoryIconChangePrompt = "Category.IconChangePrompt";
    public const string CategoryIconInvalid = "Category.IconInvalid";
    public const string CategoryIconChanged = "Category.IconChanged";
    public const string CategoryDeactivated = "Category.Deactivated";
    public const string CategoryActivated = "Category.Activated";
    public const string CategoryNotFound = "Category.NotFound";

    public const string AliasListHeader = "Alias.ListHeader";
    public const string AliasListEmpty = "Alias.ListEmpty";
    public const string AliasButtonAdd = "Alias.ButtonAdd";
    public const string AliasButtonRemovePrefix = "Alias.ButtonRemovePrefix";
    public const string AliasPrompt = "Alias.Prompt";
    public const string AliasInvalid = "Alias.Invalid";
    public const string AliasAdded = "Alias.Added";
    public const string AliasDuplicate = "Alias.Duplicate";
    public const string AliasRemoved = "Alias.Removed";
    public const string AliasConflict = "Alias.Conflict";
    public const string AliasConflictHint = "Alias.ConflictHint";
    public const string AliasButtonAddAnyway = "Alias.ButtonAddAnyway";

    public const string BudgetTitle = "Budget.Title";
    public const string BudgetEmpty = "Budget.Empty";
    public const string BudgetLine = "Budget.Line";
    public const string BudgetTotal = "Budget.Total";
    public const string BudgetButtonAssign = "Budget.ButtonAssign";
    public const string BudgetButtonCopy = "Budget.ButtonCopy";
    public const string BudgetChooseCategory = "Budget.ChooseCategory";
    public const string BudgetAmountPrompt = "Budget.AmountPrompt";
    public const string BudgetAmountInvalid = "Budget.AmountInvalid";
    public const string BudgetSaved = "Budget.Saved";
    public const string BudgetCopied = "Budget.Copied";
    public const string BudgetNoPrevious = "Budget.NoPrevious";
    public const string BudgetPastMonth = "Budget.PastMonth";
    public const string BudgetCategoryInactive = "Budget.CategoryInactive";
    public const string BudgetCategoryNotFound = "Budget.CategoryNotFound";
    public const string BudgetNoCategories = "Budget.NoCategories";
    public const string BudgetAlertNearLimit = "Budget.AlertNearLimit";
    public const string BudgetAlertExceeded = "Budget.AlertExceeded";

    public const string SummaryHeader = "Summary.Header";
    public const string SummaryTotalSpent = "Summary.TotalSpent";
    public const string SummaryTotalBudget = "Summary.TotalBudget";
    public const string SummaryLine = "Summary.Line";
    public const string SummaryLineUnbudgeted = "Summary.LineUnbudgeted";
    public const string SummaryEmpty = "Summary.Empty";
    public const string NavigationPrevious = "Navigation.Previous";
    public const string NavigationCurrent = "Navigation.Current";
    public const string NavigationNext = "Navigation.Next";

    public const string StatisticsHeader = "Statistics.Header";
    public const string StatisticsTotal = "Statistics.Total";
    public const string StatisticsExpenseCount = "Statistics.ExpenseCount";
    public const string StatisticsAverageDaily = "Statistics.AverageDaily";
    public const string StatisticsInProgress = "Statistics.InProgress";
    public const string StatisticsByCategoryHeader = "Statistics.ByCategoryHeader";
    public const string StatisticsCategoryLine = "Statistics.CategoryLine";
    public const string StatisticsDailyHeader = "Statistics.DailyHeader";
    public const string StatisticsDailyLine = "Statistics.DailyLine";
    public const string StatisticsLargestHeader = "Statistics.LargestHeader";
    public const string StatisticsLargestLine = "Statistics.LargestLine";
    public const string StatisticsComparisonHeader = "Statistics.ComparisonHeader";
    public const string StatisticsComparisonLine = "Statistics.ComparisonLine";
    public const string StatisticsChangeUp = "Statistics.ChangeUp";
    public const string StatisticsChangeDown = "Statistics.ChangeDown";
    public const string StatisticsChangeFlat = "Statistics.ChangeFlat";
    public const string StatisticsChangeUnknown = "Statistics.ChangeUnknown";
    public const string StatisticsEmpty = "Statistics.Empty";
    public const string StatisticsButtonCategoriesChart = "Statistics.ButtonCategoriesChart";
    public const string StatisticsButtonDailyChart = "Statistics.ButtonDailyChart";
    public const string StatisticsChartCategoriesCaption = "Statistics.ChartCategoriesCaption";
    public const string StatisticsChartDailyCaption = "Statistics.ChartDailyCaption";

    public const string HistoryRangeThisMonth = "History.RangeThisMonth";
    public const string HistoryRangeLastMonth = "History.RangeLastMonth";
    public const string HistoryRangeLastThreeMonths = "History.RangeLastThreeMonths";
    public const string HistoryRangeThisYear = "History.RangeThisYear";
    public const string HistoryHeader = "History.Header";
    public const string HistoryEmpty = "History.Empty";
    public const string HistoryDayTotal = "History.DayTotal";
    public const string HistoryExpenseLine = "History.ExpenseLine";
    public const string HistoryButtonMore = "History.ButtonMore";
    public const string HistoryButtonPrevious = "History.ButtonPrevious";

    public const string RecurringListHeader = "Recurring.ListHeader";
    public const string RecurringEmpty = "Recurring.Empty";
    public const string RecurringLine = "Recurring.Line";
    public const string RecurringInactiveLine = "Recurring.InactiveLine";
    public const string RecurringButtonNew = "Recurring.ButtonNew";
    public const string RecurringButtonPause = "Recurring.ButtonPause";
    public const string RecurringButtonResume = "Recurring.ButtonResume";
    public const string RecurringButtonDelete = "Recurring.ButtonDelete";
    public const string RecurringButtonDeleteConfirm = "Recurring.ButtonDeleteConfirm";
    public const string RecurringButtonBack = "Recurring.ButtonBack";
    public const string RecurringDeleteConfirm = "Recurring.DeleteConfirm";
    public const string RecurringAmountPrompt = "Recurring.AmountPrompt";
    public const string RecurringDescriptionPrompt = "Recurring.DescriptionPrompt";
    public const string RecurringDayPrompt = "Recurring.DayPrompt";
    public const string RecurringDayInvalid = "Recurring.DayInvalid";
    public const string RecurringDayLine = "Recurring.DayLine";
    public const string RecurringConfirmHeader = "Recurring.ConfirmHeader";
    public const string RecurringConfirmNote = "Recurring.ConfirmNote";
    public const string RecurringCreated = "Recurring.Created";
    public const string RecurringPaused = "Recurring.Paused";
    public const string RecurringResumed = "Recurring.Resumed";
    public const string RecurringDeleted = "Recurring.Deleted";
    public const string RecurringNotFound = "Recurring.NotFound";
    public const string RecurringDetailHeader = "Recurring.DetailHeader";
    public const string RecurringDetailStatus = "Recurring.DetailStatus";
    public const string RecurringDetailLastGenerated = "Recurring.DetailLastGenerated";
    public const string RecurringStatusActive = "Recurring.StatusActive";
    public const string RecurringStatusPaused = "Recurring.StatusPaused";
    public const string RecurringAppliedHeader = "Recurring.AppliedHeader";
    public const string RecurringAppliedLine = "Recurring.AppliedLine";

    public const string MonthJanuary = "Month.January";
    public const string MonthFebruary = "Month.February";
    public const string MonthMarch = "Month.March";
    public const string MonthApril = "Month.April";
    public const string MonthMay = "Month.May";
    public const string MonthJune = "Month.June";
    public const string MonthJuly = "Month.July";
    public const string MonthAugust = "Month.August";
    public const string MonthSeptember = "Month.September";
    public const string MonthOctober = "Month.October";
    public const string MonthNovember = "Month.November";
    public const string MonthDecember = "Month.December";

    /// <summary>Every key that must exist in every supported language.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Welcome,
        Help,
        AccessDenied,
        Cancelled,
        ConversationExpired,
        UnexpectedError,
        RateLimited,
        MenuSummary,
        MenuAddExpense,
        MenuExpenses,
        MenuRecurring,
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
        OnboardingTimezonePrompt,
        OnboardingTimezoneSaved,
        OnboardingTimezoneCustomPrompt,
        OnboardingTimezoneCustomInvalid,
        FeatureNotReady,
        CommandStartDescription,
        CommandHelpDescription,
        CommandCancelDescription,
        ExpenseRegistered,
        ExpenseConfirmationHeader,
        ExpenseCategorySuggestion,
        ExpenseCategoryAmbiguous,
        ExpenseCategoryNone,
        ExpenseDescriptionPrompt,
        ExpenseCategoryPrompt,
        ExpenseNoCategories,
        ExpenseConfirmationAmount,
        ExpenseConfirmationDescription,
        ExpenseConfirmationCategory,
        ExpenseConfirmationDate,
        ExpenseNoDescription,
        ExpenseAmbiguousAmount,
        ExpenseEditPrompt,
        ExpenseButtonAmount,
        ExpenseButtonDescription,
        ExpenseButtonCategory,
        ExpenseUpdated,
        ExpenseExpired,
        ExpenseNotFound,
        ExpenseUndone,
        ExpenseDeleted,
        ExpenseDetailHeader,
        ExpenseDeleteConfirm,
        ExpenseDeletedConfirm,
        ExpenseLearnKeywordPrompt,
        ExpenseLearnKeywordSaved,
        ButtonSkip,
        ButtonDelete,
        ButtonToday,
        ButtonYesterday,
        ButtonBack,
        ButtonSaveAlias,
        CategoryListHeader,
        CategoryListEmpty,
        CategoryButtonNew,
        CategoryButtonBudget,
        CategoryButtonBack,
        CategoryButtonRename,
        CategoryButtonIcon,
        CategoryButtonAliases,
        CategoryButtonDeactivate,
        CategoryButtonActivate,
        CategoryButtonSkip,
        CategoryInactiveMarker,
        CategoryStatusInactive,
        CategoryDetailHeader,
        CategoryNamePrompt,
        CategoryNameInvalid,
        CategoryNameTaken,
        CategoryCreated,
        CategoryReactivated,
        CategoryRenamePrompt,
        CategoryRenamed,
        CategoryIconPrompt,
        CategoryIconChangePrompt,
        CategoryIconInvalid,
        CategoryIconChanged,
        CategoryDeactivated,
        CategoryActivated,
        CategoryNotFound,
        AliasListHeader,
        AliasListEmpty,
        AliasButtonAdd,
        AliasButtonRemovePrefix,
        AliasPrompt,
        AliasInvalid,
        AliasAdded,
        AliasDuplicate,
        AliasRemoved,
        AliasConflict,
        AliasConflictHint,
        AliasButtonAddAnyway,
        BudgetTitle,
        BudgetEmpty,
        BudgetLine,
        BudgetTotal,
        BudgetButtonAssign,
        BudgetButtonCopy,
        BudgetChooseCategory,
        BudgetAmountPrompt,
        BudgetAmountInvalid,
        BudgetSaved,
        BudgetCopied,
        BudgetNoPrevious,
        BudgetPastMonth,
        BudgetCategoryInactive,
        BudgetCategoryNotFound,
        BudgetNoCategories,
        BudgetAlertNearLimit,
        BudgetAlertExceeded,
        SummaryHeader,
        SummaryTotalSpent,
        SummaryTotalBudget,
        SummaryLine,
        SummaryLineUnbudgeted,
        SummaryEmpty,
        NavigationPrevious,
        NavigationCurrent,
        NavigationNext,
        StatisticsHeader,
        StatisticsTotal,
        StatisticsExpenseCount,
        StatisticsAverageDaily,
        StatisticsInProgress,
        StatisticsByCategoryHeader,
        StatisticsCategoryLine,
        StatisticsDailyHeader,
        StatisticsDailyLine,
        StatisticsLargestHeader,
        StatisticsLargestLine,
        StatisticsComparisonHeader,
        StatisticsComparisonLine,
        StatisticsChangeUp,
        StatisticsChangeDown,
        StatisticsChangeFlat,
        StatisticsChangeUnknown,
        StatisticsEmpty,
        StatisticsButtonCategoriesChart,
        StatisticsButtonDailyChart,
        StatisticsChartCategoriesCaption,
        StatisticsChartDailyCaption,
        HistoryRangeThisMonth,
        HistoryRangeLastMonth,
        HistoryRangeLastThreeMonths,
        HistoryRangeThisYear,
        HistoryHeader,
        HistoryEmpty,
        HistoryDayTotal,
        HistoryExpenseLine,
        HistoryButtonMore,
        HistoryButtonPrevious,
        RecurringListHeader,
        RecurringEmpty,
        RecurringLine,
        RecurringInactiveLine,
        RecurringButtonNew,
        RecurringButtonPause,
        RecurringButtonResume,
        RecurringButtonDelete,
        RecurringButtonDeleteConfirm,
        RecurringButtonBack,
        RecurringDeleteConfirm,
        RecurringAmountPrompt,
        RecurringDescriptionPrompt,
        RecurringDayPrompt,
        RecurringDayInvalid,
        RecurringDayLine,
        RecurringConfirmHeader,
        RecurringConfirmNote,
        RecurringCreated,
        RecurringPaused,
        RecurringResumed,
        RecurringDeleted,
        RecurringNotFound,
        RecurringDetailHeader,
        RecurringDetailStatus,
        RecurringDetailLastGenerated,
        RecurringStatusActive,
        RecurringStatusPaused,
        RecurringAppliedHeader,
        RecurringAppliedLine,
        MonthJanuary,
        MonthFebruary,
        MonthMarch,
        MonthApril,
        MonthMay,
        MonthJune,
        MonthJuly,
        MonthAugust,
        MonthSeptember,
        MonthOctober,
        MonthNovember,
        MonthDecember,
    ];

    /// <summary>Month names in calendar order, so a period can be rendered from the catalog.</summary>
    public static IReadOnlyList<string> Months { get; } =
    [
        MonthJanuary,
        MonthFebruary,
        MonthMarch,
        MonthApril,
        MonthMay,
        MonthJune,
        MonthJuly,
        MonthAugust,
        MonthSeptember,
        MonthOctober,
        MonthNovember,
        MonthDecember,
    ];
}
