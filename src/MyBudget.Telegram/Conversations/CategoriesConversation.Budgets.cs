using MyBudget.Application.Budgets;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The monthly budget slice of the category flow: see the month, assign an amount to a category,
/// and choose whether it applies to one month or to every month.
/// <para>
/// Only the current month is offered, and the service refuses a past one with a reason, so the
/// interface says past months cannot be edited rather than silently doing nothing. The first
/// assignment a user ever makes becomes the recurring default; after that the scope is always
/// asked, and nothing is ever copied automatically.
/// </para>
/// </summary>
internal sealed partial class CategoriesConversation
{
    private const string BudgetCallback = CallbackPrefix + "budget";
    private const string BudgetAssignCallback = CallbackPrefix + "budget:assign";
    private const string BudgetSetPrefix = CallbackPrefix + "budget:set:";
    private const string BudgetScopeMonthCallback = CallbackPrefix + "budget:scope:month";
    private const string BudgetScopeAllCallback = CallbackPrefix + "budget:scope:all";

    private const string BudgetScreenState = "budget";
    private const string BudgetCategoryState = "budget-category";
    private const string AwaitingBudgetAmountState = "awaiting-budget-amount";
    private const string BudgetScopeState = "budget-scope";

    private async Task<ConversationTurn> BuildBudgetScreenAsync(
        ConversationContext context,
        CancellationToken cancellationToken,
        IReadOnlyList<BotResponse> notices)
    {
        var language = context.Language;
        var period = CurrentPeriod(context);
        var view = await budgets.GetMonthAsync(context.User.Id, period, cancellationToken);
        var allocated = view.Lines.Where(line => line.Amount > 0).ToList();

        var text = messages.Get(language, MessageKeys.BudgetTitle, MonthLabel(language, period));

        if (allocated.Count == 0)
        {
            text += "\n" + messages.Get(language, MessageKeys.BudgetEmpty);
        }
        else
        {
            foreach (var line in allocated)
            {
                text += "\n" + messages.Get(
                    language,
                    line.IsRecurring ? MessageKeys.BudgetLineRecurring : MessageKeys.BudgetLine,
                    line.Icon,
                    line.CategoryName,
                    moneyFormatter.Format(line.Amount, context.User.Currency));
            }

            text += "\n" + messages.Get(
                language,
                MessageKeys.BudgetTotal,
                moneyFormatter.Format(view.TotalAllocated, context.User.Currency));
        }

        var keyboard = BotKeyboard.Inline(
        [
            [new BotButton(messages.Get(language, MessageKeys.BudgetButtonAssign), BudgetAssignCallback)],
            [new BotButton(messages.Get(language, MessageKeys.CategoryButtonBack), ListCallback)],
        ]);

        var responses = new List<BotResponse>(notices) { BotResponse.Message(text, keyboard) };

        return new ConversationTurn(responses)
        {
            NextState = BudgetScreenState,
            NextPayload = new CategoriesPayload().Serialize(),
        };
    }

    private async Task<ConversationTurn> BuildBudgetCategoryPickerAsync(
        ConversationContext context,
        CancellationToken cancellationToken,
        IReadOnlyList<BotResponse> notices)
    {
        var language = context.Language;
        var period = CurrentPeriod(context);
        var view = await budgets.GetMonthAsync(context.User.Id, period, cancellationToken);
        var active = view.Lines.Where(line => line.IsActive).ToList();

        if (active.Count == 0)
        {
            return await BuildBudgetScreenAsync(
                context, cancellationToken,
                [.. notices, Said(context, MessageKeys.BudgetNoCategories)]);
        }

        var rows = active
            .Select(line => new[]
            {
                new BotButton($"{line.Icon} {line.CategoryName}", BudgetSetPrefix + line.CategoryId),
            })
            .ToList();
        rows.Add([new BotButton(messages.Get(language, MessageKeys.CategoryButtonBack), BudgetCallback)]);

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(
                messages.Get(language, MessageKeys.BudgetChooseCategory),
                BotKeyboard.Inline([.. rows])),
        };

        return new ConversationTurn(responses)
        {
            NextState = BudgetCategoryState,
            NextPayload = new CategoriesPayload().Serialize(),
        };
    }

    private async Task<ConversationTurn> HandleBudgetAmountAsync(
        ConversationContext context, CategoriesPayload payload, string rawAmount,
        CancellationToken cancellationToken)
    {
        if (payload.CategoryId is not { } categoryId)
        {
            return await BuildBudgetScreenAsync(context, cancellationToken, []);
        }

        if (moneyParser.Parse(rawAmount, context.User.Currency) is not MoneyParseResult.Success amount)
        {
            // Ambiguity is treated exactly like an invalid amount here: the value is echoed and
            // the user simply types it again. No separate magnitude confirmation state.
            return PromptTurn(
                context, AwaitingBudgetAmountState, payload,
                MessageKeys.BudgetAmountPrompt, false, BudgetCallback,
                [Said(context, MessageKeys.BudgetAmountInvalid)],
                [payload.Name ?? string.Empty]);
        }

        // The first budget a user ever sets becomes the recurring default automatically; from
        // then on the scope is an explicit decision.
        if (!await budgets.HasDefaultsAsync(context.User.Id, cancellationToken))
        {
            return await ApplyAsync(
                context, payload, amount.Amount, BudgetScope.AllMonths, cancellationToken);
        }

        return BuildScopePrompt(context, payload with { Amount = amount.Amount });
    }

    private ConversationTurn BuildScopePrompt(ConversationContext context, CategoriesPayload payload)
    {
        var language = context.Language;

        var keyboard = BotKeyboard.Inline(
        [
            [new BotButton(messages.Get(language, MessageKeys.BudgetScopeMonth), BudgetScopeMonthCallback)],
            [new BotButton(messages.Get(language, MessageKeys.BudgetScopeAllMonths), BudgetScopeAllCallback)],
            [new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback)],
        ]);

        var prompt = messages.Get(
            language, MessageKeys.BudgetScopePrompt, MonthLabel(language, CurrentPeriod(context)));

        return new ConversationTurn([BotResponse.Message(prompt, keyboard)])
        {
            NextState = BudgetScopeState,
            NextPayload = payload.Serialize(),
        };
    }

    private async Task<ConversationTurn> ApplyAsync(
        ConversationContext context,
        CategoriesPayload payload,
        long amount,
        BudgetScope scope,
        CancellationToken cancellationToken)
    {
        if (payload.CategoryId is not { } categoryId)
        {
            return await BuildBudgetScreenAsync(context, cancellationToken, []);
        }

        var result = await budgets.SetAllocationAsync(
            context.User.Id, CurrentPeriod(context), categoryId, amount, scope,
            localDate.Today(context.User.TimeZone), cancellationToken);

        var notice = result.Status switch
        {
            BudgetWriteStatus.Saved => Said(
                context,
                scope == BudgetScope.AllMonths ? MessageKeys.BudgetSavedRecurring : MessageKeys.BudgetSaved,
                moneyFormatter.Format(amount, context.User.Currency),
                payload.Name ?? string.Empty),
            BudgetWriteStatus.CategoryInactive => Said(context, MessageKeys.BudgetCategoryInactive),
            BudgetWriteStatus.CategoryNotFound => Said(context, MessageKeys.BudgetCategoryNotFound),
            _ => Said(context, MessageKeys.BudgetPastMonth),
        };

        return await BuildBudgetScreenAsync(context, cancellationToken, [notice]);
    }

    private async Task<ConversationTurn?> HandleBudgetCallbackAsync(
        ConversationContext context, string data, CategoriesPayload payload,
        CancellationToken cancellationToken)
    {
        if (data == BudgetCallback)
        {
            return await BuildBudgetScreenAsync(context, cancellationToken, []);
        }

        if (data == BudgetAssignCallback)
        {
            return await BuildBudgetCategoryPickerAsync(context, cancellationToken, []);
        }

        if (data == BudgetScopeMonthCallback || data == BudgetScopeAllCallback)
        {
            if (payload is { CategoryId: not null, Amount: { } amount })
            {
                var scope = data == BudgetScopeAllCallback ? BudgetScope.AllMonths : BudgetScope.Month;
                return await ApplyAsync(context, payload, amount, scope, cancellationToken);
            }

            return await BuildBudgetScreenAsync(context, cancellationToken, []);
        }

        if (data.StartsWith(BudgetSetPrefix, StringComparison.Ordinal)
            && Guid.TryParse(data[BudgetSetPrefix.Length..], out var categoryId))
        {
            var category = await categories.GetAsync(context.User.Id, categoryId, cancellationToken);
            if (category is null)
            {
                return await BuildBudgetScreenAsync(
                    context, cancellationToken, [Said(context, MessageKeys.BudgetCategoryNotFound)]);
            }

            var draft = new CategoriesPayload { CategoryId = categoryId, Name = category.Name };

            return PromptTurn(
                context, AwaitingBudgetAmountState, draft,
                MessageKeys.BudgetAmountPrompt, false, BudgetCallback,
                [],
                [category.Name]);
        }

        return null;
    }

    private MonthPeriod CurrentPeriod(ConversationContext context) =>
        MonthPeriod.FromDate(localDate.Today(context.User.TimeZone));

    private string MonthLabel(string language, MonthPeriod period) =>
        $"{messages.Get(language, MessageKeys.Months[period.Month - 1])} {period.Year}";
}
