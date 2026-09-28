using MyBudget.Application.Dates;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The month's expenses: list, detail and delete.
/// <para>
/// The list opens the detail; the detail offers delete behind a confirmation. Editing is a
/// separate slice of the same flow.
/// </para>
/// </summary>
internal sealed class ExpensesConversation(
    IUserMessages messages,
    IExpenseService expenses,
    IMoneyFormatter moneyFormatter,
    IUserLocalDate localDate,
    MainMenu menu) : IConversation
{
    public const string ConversationName = "expenses";

    private const string ListCallback = "exps:list";
    private const string OpenPrefix = "exps:open:";
    private const string DeleteCallback = "exps:delete";
    private const string ConfirmDeleteCallback = "exps:delete!";
    private const string CancelCallback = "exps:cancel";

    private const string ListState = "list";
    private const string DetailState = "detail";
    private const string DeleteConfirmState = "delete-confirm";

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(
        ConversationContext context, CancellationToken cancellationToken) =>
        BuildListAsync(context, [], cancellationToken);

    public Task<ConversationTurn> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken) =>
        BuildListAsync(context, [], cancellationToken);

    public async Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        var data = callback.Data;
        var payload = ExpensesPayload.Parse(context.Conversation?.Payload);

        if (data == CancelCallback)
        {
            return Cancelled(context);
        }

        if (data == ListCallback)
        {
            return await BuildListAsync(context, [], cancellationToken);
        }

        if (data == DeleteCallback)
        {
            return payload.ExpenseId is { } deleteId
                ? DeleteConfirmation(context, deleteId)
                : await BuildListAsync(context, [], cancellationToken);
        }

        if (data == ConfirmDeleteCallback)
        {
            return payload.ExpenseId is { } confirmedId
                ? await DeleteAsync(context, confirmedId, cancellationToken)
                : await BuildListAsync(context, [], cancellationToken);
        }

        if (data.StartsWith(OpenPrefix, StringComparison.Ordinal)
            && Guid.TryParse(data[OpenPrefix.Length..], out var openId))
        {
            return await BuildDetailAsync(context, openId, [], cancellationToken);
        }

        return await BuildListAsync(context, [], cancellationToken);
    }

    private async Task<ConversationTurn> BuildListAsync(
        ConversationContext context,
        IReadOnlyList<BotResponse> notices,
        CancellationToken cancellationToken)
    {
        var language = context.Language;
        var period = CurrentPeriod(context);
        var items = await expenses.ListMonthAsync(context.User.Id, period, cancellationToken);

        if (items.Count == 0)
        {
            var empty = new List<BotResponse>(notices)
            {
                BotResponse.Message(
                    messages.Get(language, MessageKeys.ExpenseListEmpty),
                    menu.ReplyKeyboard(language)),
            };

            return new ConversationTurn(empty)
            {
                Completed = true,
            };
        }

        var rows = items
            .Select(item => new[] { new BotButton(ListLabel(context, item), OpenPrefix + item.Id) })
            .ToList();

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(
                messages.Get(language, MessageKeys.ExpenseListHeader, MonthLabel(context, period)),
                BotKeyboard.Inline([.. rows])),
        };

        return new ConversationTurn(responses)
        {
            NextState = ListState,
            NextPayload = new ExpensesPayload().Serialize(),
        };
    }

    private async Task<ConversationTurn> BuildDetailAsync(
        ConversationContext context,
        Guid expenseId,
        IReadOnlyList<BotResponse> notices,
        CancellationToken cancellationToken)
    {
        var item = await expenses.GetItemAsync(context.User.Id, expenseId, cancellationToken);
        if (item is null)
        {
            return await BuildListAsync(
                context,
                [.. notices, Said(context, MessageKeys.ExpenseNotFound)],
                cancellationToken);
        }

        var language = context.Language;
        var keyboard = BotKeyboard.Inline(
            new[] { new BotButton(messages.Get(language, MessageKeys.ButtonDelete), DeleteCallback) },
            new[] { new BotButton(messages.Get(language, MessageKeys.ButtonBack), ListCallback) });

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(DetailText(context, item), keyboard),
        };

        return new ConversationTurn(responses)
        {
            NextState = DetailState,
            NextPayload = new ExpensesPayload().WithExpense(expenseId).Serialize(),
        };
    }

    private ConversationTurn DeleteConfirmation(ConversationContext context, Guid expenseId)
    {
        var language = context.Language;
        var keyboard = BotKeyboard.Inline(
            new[] { new BotButton(messages.Get(language, MessageKeys.ButtonDelete), ConfirmDeleteCallback) },
            new[] { new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback) });

        return new ConversationTurn(
        [
            BotResponse.Message(messages.Get(language, MessageKeys.ExpenseDeleteConfirm), keyboard),
        ])
        {
            NextState = DeleteConfirmState,
            NextPayload = new ExpensesPayload().WithExpense(expenseId).Serialize(),
        };
    }

    private async Task<ConversationTurn> DeleteAsync(
        ConversationContext context, Guid expenseId, CancellationToken cancellationToken)
    {
        var deleted = await expenses.DeleteAsync(context.User.Id, expenseId, cancellationToken);
        var key = deleted ? MessageKeys.ExpenseDeletedConfirm : MessageKeys.ExpenseNotFound;

        return await BuildListAsync(context, [Said(context, key)], cancellationToken);
    }

    private string DetailText(ConversationContext context, ExpenseListItem item)
    {
        var language = context.Language;
        var description = string.IsNullOrWhiteSpace(item.Description)
            ? messages.Get(language, MessageKeys.ExpenseNoDescription)
            : item.Description;

        return string.Join(
            "\n",
            messages.Get(language, MessageKeys.ExpenseDetailHeader),
            messages.Get(
                language,
                MessageKeys.ExpenseConfirmationAmount,
                moneyFormatter.Format(item.Amount, context.User.Currency)),
            messages.Get(language, MessageKeys.ExpenseConfirmationDescription, description),
            messages.Get(
                language,
                MessageKeys.ExpenseConfirmationCategory,
                $"{item.Icon} {item.CategoryName}"),
            messages.Get(
                language,
                MessageKeys.ExpenseConfirmationDate,
                DateLabel(context, item.ExpenseDate)));
    }

    private string ListLabel(ConversationContext context, ExpenseListItem item) =>
        $"{item.ExpenseDate.Day}/{item.ExpenseDate.Month} {item.Icon} {moneyFormatter.Format(item.Amount, context.User.Currency)}";

    private ConversationTurn Cancelled(ConversationContext context) =>
        new([BotResponse.Message(
            messages.Get(context.Language, MessageKeys.Cancelled),
            menu.ReplyKeyboard(context.Language))])
        {
            Completed = true,
        };

    private DateOnly Today(ConversationContext context) => localDate.Today(context.User.TimeZone);

    private MonthPeriod CurrentPeriod(ConversationContext context) => MonthPeriod.FromDate(Today(context));

    private string MonthLabel(ConversationContext context, MonthPeriod period) =>
        $"{messages.Get(context.Language, MessageKeys.Months[period.Month - 1])} {period.Year}";

    private string DateLabel(ConversationContext context, DateOnly date) =>
        $"{date.Day} de {messages.Get(context.Language, MessageKeys.Months[date.Month - 1])} de {date.Year}";

    private BotResponse Said(ConversationContext context, string key, params object?[] args) =>
        BotResponse.Message(messages.Get(context.Language, key, args));
}
