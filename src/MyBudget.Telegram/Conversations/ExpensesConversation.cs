using System.Globalization;
using MyBudget.Application.Categories;
using MyBudget.Application.Dates;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Reporting;
using MyBudget.Domain.Budgets;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The expense history: a date range, grouped by day, paged with a keyset cursor, with the
/// detail and edit screens off each listed expense.
/// <para>
/// The list opens the detail; the detail offers delete behind a confirmation. Editing is a
/// separate slice of the same flow.
/// </para>
/// </summary>
internal sealed class ExpensesConversation(
    IUserMessages messages,
    IExpenseService expenses,
    IReportService reports,
    ICategoryService categories,
    IMoneyParser moneyParser,
    IMoneyFormatter moneyFormatter,
    IDateParser dateParser,
    IUserLocalDate localDate,
    MainMenu menu) : IConversation
{
    public const string ConversationName = "expenses";

    private const string ListCallback = "exps:list";
    private const string DetailCallback = "exps:detail";
    private const string OpenPrefix = "exps:open:";
    private const string DeleteCallback = "exps:delete";
    private const string ConfirmDeleteCallback = "exps:delete!";
    private const string CancelCallback = "exps:cancel";
    private const string EditCallback = "exps:edit";
    private const string EditAmountCallback = "exps:edit:amount";
    private const string EditDescriptionCallback = "exps:edit:description";
    private const string EditCategoryCallback = "exps:edit:category";
    private const string EditDateCallback = "exps:edit:date";
    private const string CategoryPrefix = "exps:cat:";
    private const string SkipCallback = "exps:skip";
    private const string TodayCallback = "exps:today";
    private const string YesterdayCallback = "exps:yesterday";
    private const string MoreCallback = "exps:more";
    private const string PreviousPageCallback = "exps:prevpage";
    private const string RangeThisMonthCallback = "exps:range:month";
    private const string RangeLastMonthCallback = "exps:range:prev";
    private const string RangeLastThreeMonthsCallback = "exps:range:3m";
    private const string RangeThisYearCallback = "exps:range:year";

    /// <summary>How many expenses one history page shows before "see more".</summary>
    private const int PageSize = 10;

    private const string ListState = "list";
    private const string DetailState = "detail";
    private const string DeleteConfirmState = "delete-confirm";
    private const string EditMenuState = "edit-menu";
    private const string EditAmountState = "edit-amount";
    private const string EditDescriptionState = "edit-description";
    private const string EditCategoryState = "edit-category";
    private const string EditDateState = "edit-date";

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(
        ConversationContext context, CancellationToken cancellationToken) =>
        BuildListAsync(context, [], cancellationToken);

    public Task<ConversationTurn> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken)
    {
        var payload = ExpensesPayload.Parse(context.Conversation?.Payload);

        return context.CurrentState switch
        {
            EditAmountState => HandleEditAmountAsync(context, payload, text.Text, cancellationToken),
            EditDescriptionState => HandleEditDescriptionAsync(context, payload, text.Text, cancellationToken),
            EditDateState => HandleEditDateAsync(context, payload, text.Text, cancellationToken),
            _ => BuildListAsync(context, [], cancellationToken),
        };
    }

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

        if (data == MoreCallback)
        {
            return payload.Next is null
                ? await BuildListAsync(context, [], cancellationToken)
                : await NextPageAsync(context, payload, cancellationToken);
        }

        if (data == PreviousPageCallback)
        {
            return await PreviousPageAsync(context, payload, cancellationToken);
        }

        if (RangeFor(context, data) is { } range)
        {
            return await BuildListAsync(context, payload.WithRange(range), [], cancellationToken);
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

        if (data == DetailCallback)
        {
            return payload.ExpenseId is { } detailId
                ? await BuildDetailAsync(context, detailId, [], cancellationToken)
                : await BuildListAsync(context, [], cancellationToken);
        }

        if (data == EditCallback)
        {
            return payload.ExpenseId is { } editId
                ? EditMenu(context, editId)
                : await BuildListAsync(context, [], cancellationToken);
        }

        if (data == EditAmountCallback)
        {
            return EditAmountPrompt(context, payload, []);
        }

        if (data == EditDescriptionCallback)
        {
            return EditDescriptionPrompt(context, payload, []);
        }

        if (data == EditCategoryCallback)
        {
            return await EditCategoryPickerAsync(context, payload, [], cancellationToken);
        }

        if (data == EditDateCallback)
        {
            return EditDatePrompt(context, payload, []);
        }

        if (data == SkipCallback)
        {
            return payload.ExpenseId is { } skipId
                ? await ApplyUpdateAsync(
                    context, skipId, clearDescription: true, cancellationToken: cancellationToken)
                : await BuildListAsync(context, [], cancellationToken);
        }

        if (data == TodayCallback)
        {
            return payload.ExpenseId is { } todayId
                ? await ApplyUpdateAsync(
                    context, todayId, date: Today(context), cancellationToken: cancellationToken)
                : await BuildListAsync(context, [], cancellationToken);
        }

        if (data == YesterdayCallback)
        {
            return payload.ExpenseId is { } yesterdayId
                ? await ApplyUpdateAsync(
                    context, yesterdayId, date: Today(context).AddDays(-1),
                    cancellationToken: cancellationToken)
                : await BuildListAsync(context, [], cancellationToken);
        }

        if (data.StartsWith(CategoryPrefix, StringComparison.Ordinal)
            && Guid.TryParse(data[CategoryPrefix.Length..], out var chosenId))
        {
            return payload.ExpenseId is { } categoryEditId
                ? await ApplyUpdateAsync(
                    context, categoryEditId, categoryId: chosenId, cancellationToken: cancellationToken)
                : await BuildListAsync(context, [], cancellationToken);
        }

        if (data.StartsWith(OpenPrefix, StringComparison.Ordinal)
            && Guid.TryParse(data[OpenPrefix.Length..], out var openId))
        {
            return await BuildDetailAsync(context, openId, [], cancellationToken);
        }

        return await BuildListAsync(context, [], cancellationToken);
    }

    private Task<ConversationTurn> BuildListAsync(
        ConversationContext context,
        IReadOnlyList<BotResponse> notices,
        CancellationToken cancellationToken) =>
        BuildListAsync(
            context,
            ExpensesPayload.Parse(context.Conversation?.Payload),
            notices,
            cancellationToken);

    private async Task<ConversationTurn> BuildListAsync(
        ConversationContext context,
        ExpensesPayload payload,
        IReadOnlyList<BotResponse> notices,
        CancellationToken cancellationToken)
    {
        var language = context.Language;
        var range = payload.Range ?? CurrentRange(context);

        // Fetch one extra row inside the service so HasMore is known without a COUNT.
        var page = await reports.GetHistoryAsync(
            context.User.Id, range, payload.After, PageSize, cancellationToken);

        var rows = page.Items
            .Select(item => new[] { new BotButton(ListLabel(context, item), OpenPrefix + item.Id) })
            .ToList();

        var navigation = new List<BotButton>();
        if (payload.After is not null)
        {
            navigation.Add(new BotButton(
                messages.Get(language, MessageKeys.HistoryButtonPrevious), PreviousPageCallback));
        }

        if (page.HasMore)
        {
            navigation.Add(new BotButton(
                messages.Get(language, MessageKeys.HistoryButtonMore), MoreCallback));
        }

        if (navigation.Count > 0)
        {
            rows.Add([.. navigation]);
        }

        rows.Add(
        [
            new BotButton(messages.Get(language, MessageKeys.HistoryRangeThisMonth), RangeThisMonthCallback),
            new BotButton(messages.Get(language, MessageKeys.HistoryRangeLastMonth), RangeLastMonthCallback),
        ]);
        rows.Add(
        [
            new BotButton(
                messages.Get(language, MessageKeys.HistoryRangeLastThreeMonths), RangeLastThreeMonthsCallback),
            new BotButton(messages.Get(language, MessageKeys.HistoryRangeThisYear), RangeThisYearCallback),
        ]);

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(RenderHistory(context, range, page), BotKeyboard.Inline([.. rows])),
        };

        return new ConversationTurn(responses)
        {
            NextState = ListState,
            NextPayload = payload.WithPage(payload.After, page.NextCursor, payload.Back).Serialize(),
        };
    }

    private Task<ConversationTurn> NextPageAsync(
        ConversationContext context, ExpensesPayload payload, CancellationToken cancellationToken)
    {
        // Step forward: the page just shown becomes the cursor we came from, and the stack grows
        // only when there was a real cursor (the first page has none).
        var back = payload.Back.ToList();
        if (payload.After is { } after)
        {
            back.Add(after);
        }

        var next = payload with
        {
            AfterDate = payload.Next?.ExpenseDate,
            AfterId = payload.Next?.ExpenseId,
            NextDate = null,
            NextId = null,
            Back = back,
        };

        return BuildListAsync(context, next, [], cancellationToken);
    }

    private Task<ConversationTurn> PreviousPageAsync(
        ConversationContext context, ExpensesPayload payload, CancellationToken cancellationToken)
    {
        var back = payload.Back.ToList();
        ExpensePageCursor? after = null;
        if (back.Count > 0)
        {
            after = back[^1];
            back.RemoveAt(back.Count - 1);
        }

        var previous = payload with
        {
            AfterDate = after?.ExpenseDate,
            AfterId = after?.ExpenseId,
            NextDate = null,
            NextId = null,
            Back = back,
        };

        return BuildListAsync(context, previous, [], cancellationToken);
    }

    private DateRange? RangeFor(ConversationContext context, string data)
    {
        var period = CurrentPeriod(context);
        return data switch
        {
            RangeThisMonthCallback => DateRange.ForMonth(period),
            RangeLastMonthCallback => DateRange.ForMonth(period.Previous),
            RangeLastThreeMonthsCallback => new DateRange(
                period.Previous.Previous.FirstDay, period.LastDay),
            RangeThisYearCallback => new DateRange(
                new DateOnly(period.Year, 1, 1), new DateOnly(period.Year, 12, 31)),
            _ => null,
        };
    }

    private string RenderHistory(
        ConversationContext context, DateRange range, ExpenseHistoryPage page)
    {
        var language = context.Language;
        var lines = new List<string>
        {
            messages.Get(language, MessageKeys.HistoryHeader, RangeLabel(range)),
        };

        if (page.Items.Count == 0)
        {
            lines.Add(string.Empty);
            lines.Add(messages.Get(language, MessageKeys.HistoryEmpty));
            return string.Join("\n", lines);
        }

        lines.Add(string.Empty);
        var first = true;
        foreach (var day in page.Items
                     .GroupBy(item => item.ExpenseDate)
                     .OrderByDescending(group => group.Key))
        {
            if (!first)
            {
                lines.Add(string.Empty);
            }

            first = false;
            lines.Add(DateLabel(context, day.Key));

            foreach (var item in day)
            {
                var description = string.IsNullOrWhiteSpace(item.Description)
                    ? messages.Get(language, MessageKeys.ExpenseNoDescription)
                    : item.Description;

                lines.Add(messages.Get(
                    language,
                    MessageKeys.HistoryExpenseLine,
                    description,
                    moneyFormatter.Format(item.Amount, context.User.Currency)));
            }

            lines.Add(messages.Get(
                language,
                MessageKeys.HistoryDayTotal,
                moneyFormatter.Format(day.Sum(item => item.Amount), context.User.Currency)));
        }

        return string.Join("\n", lines);
    }

    private static string RangeLabel(DateRange range) =>
        $"{range.From.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}"
        + $" – {range.To.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)}";

    private DateRange CurrentRange(ConversationContext context) =>
        DateRange.ForMonth(CurrentPeriod(context));

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
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonEdit), EditCallback),
                new BotButton(messages.Get(language, MessageKeys.ButtonDelete), DeleteCallback),
            },
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

    private ConversationTurn EditMenu(ConversationContext context, Guid expenseId)
    {
        var language = context.Language;
        var keyboard = BotKeyboard.Inline(
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ExpenseButtonAmount), EditAmountCallback),
                new BotButton(
                    messages.Get(language, MessageKeys.ExpenseButtonDescription),
                    EditDescriptionCallback),
            },
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ExpenseButtonCategory), EditCategoryCallback),
                new BotButton(messages.Get(language, MessageKeys.ButtonChangeDate), EditDateCallback),
            },
            new[] { new BotButton(messages.Get(language, MessageKeys.ButtonBack), DetailCallback) });

        return new ConversationTurn(
        [
            BotResponse.Message(messages.Get(language, MessageKeys.ExpenseEditPrompt), keyboard),
        ])
        {
            NextState = EditMenuState,
            NextPayload = new ExpensesPayload().WithExpense(expenseId).Serialize(),
        };
    }

    private ConversationTurn EditAmountPrompt(
        ConversationContext context, ExpensesPayload payload, IReadOnlyList<BotResponse> notices)
    {
        var language = context.Language;
        var keyboard = BotKeyboard.Inline(new[]
        {
            new BotButton(messages.Get(language, MessageKeys.ButtonBack), EditCallback),
        });

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(messages.Get(language, MessageKeys.AmountPrompt), keyboard),
        };

        return new ConversationTurn(responses)
        {
            NextState = EditAmountState,
            NextPayload = payload.Serialize(),
        };
    }

    private async Task<ConversationTurn> HandleEditAmountAsync(
        ConversationContext context, ExpensesPayload payload, string raw, CancellationToken cancellationToken)
    {
        if (payload.ExpenseId is not { } expenseId)
        {
            return await BuildListAsync(context, [], cancellationToken);
        }

        var parsed = moneyParser.Parse(raw, context.User.Currency);
        if (parsed is not MoneyParseResult.Success amount)
        {
            var key = parsed is MoneyParseResult.Invalid invalid
                ? InputErrorMessages.ForAmount(invalid.Reason)
                : MessageKeys.AmountInvalid;

            return EditAmountPrompt(context, payload, [Said(context, key)]);
        }

        return await ApplyUpdateAsync(context, expenseId, amount: amount.Amount, cancellationToken: cancellationToken);
    }

    private ConversationTurn EditDescriptionPrompt(
        ConversationContext context, ExpensesPayload payload, IReadOnlyList<BotResponse> notices)
    {
        var language = context.Language;
        var keyboard = BotKeyboard.Inline(
            new[] { new BotButton(messages.Get(language, MessageKeys.ButtonSkip), SkipCallback) },
            new[] { new BotButton(messages.Get(language, MessageKeys.ButtonBack), EditCallback) });

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(messages.Get(language, MessageKeys.ExpenseDescriptionPrompt), keyboard),
        };

        return new ConversationTurn(responses)
        {
            NextState = EditDescriptionState,
            NextPayload = payload.Serialize(),
        };
    }

    private async Task<ConversationTurn> HandleEditDescriptionAsync(
        ConversationContext context, ExpensesPayload payload, string raw, CancellationToken cancellationToken)
    {
        if (payload.ExpenseId is not { } expenseId)
        {
            return await BuildListAsync(context, [], cancellationToken);
        }

        return string.IsNullOrWhiteSpace(raw)
            ? await ApplyUpdateAsync(context, expenseId, clearDescription: true, cancellationToken: cancellationToken)
            : await ApplyUpdateAsync(context, expenseId, description: raw.Trim(), cancellationToken: cancellationToken);
    }

    private async Task<ConversationTurn> EditCategoryPickerAsync(
        ConversationContext context, ExpensesPayload payload, IReadOnlyList<BotResponse> notices,
        CancellationToken cancellationToken)
    {
        var language = context.Language;
        var active = await categories.ListAsync(context.User.Id, includeInactive: false, cancellationToken);

        var rows = active
            .Select(category => new[]
            {
                new BotButton($"{category.Icon} {category.Name}", CategoryPrefix + category.Id),
            })
            .ToList();
        rows.Add([new BotButton(messages.Get(language, MessageKeys.ButtonBack), EditCallback)]);

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(
                messages.Get(language, MessageKeys.ExpenseCategoryPrompt),
                BotKeyboard.Inline([.. rows])),
        };

        return new ConversationTurn(responses)
        {
            NextState = EditCategoryState,
            NextPayload = payload.Serialize(),
        };
    }

    private ConversationTurn EditDatePrompt(
        ConversationContext context, ExpensesPayload payload, IReadOnlyList<BotResponse> notices)
    {
        var language = context.Language;
        var keyboard = BotKeyboard.Inline(
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonToday), TodayCallback),
                new BotButton(messages.Get(language, MessageKeys.ButtonYesterday), YesterdayCallback),
            },
            new[] { new BotButton(messages.Get(language, MessageKeys.ButtonBack), EditCallback) });

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(messages.Get(language, MessageKeys.DatePrompt), keyboard),
        };

        return new ConversationTurn(responses)
        {
            NextState = EditDateState,
            NextPayload = payload.Serialize(),
        };
    }

    private async Task<ConversationTurn> HandleEditDateAsync(
        ConversationContext context, ExpensesPayload payload, string raw, CancellationToken cancellationToken)
    {
        if (payload.ExpenseId is not { } expenseId)
        {
            return await BuildListAsync(context, [], cancellationToken);
        }

        var parsed = dateParser.Parse(raw, Today(context));
        if (parsed is not DateParseResult.Success success)
        {
            var key = parsed is DateParseResult.Invalid invalid
                ? InputErrorMessages.ForDate(invalid.Reason)
                : MessageKeys.DateInvalid;

            return EditDatePrompt(context, payload, [Said(context, key)]);
        }

        return await ApplyUpdateAsync(context, expenseId, date: success.Date, cancellationToken: cancellationToken);
    }

    private async Task<ConversationTurn> ApplyUpdateAsync(
        ConversationContext context,
        Guid expenseId,
        long? amount = null,
        string? description = null,
        Guid? categoryId = null,
        DateOnly? date = null,
        bool clearDescription = false,
        CancellationToken cancellationToken = default)
    {
        var item = await expenses.GetItemAsync(context.User.Id, expenseId, cancellationToken);
        if (item is null)
        {
            return await BuildListAsync(
                context, [Said(context, MessageKeys.ExpenseNotFound)], cancellationToken);
        }

        var newDescription = clearDescription ? null : description ?? item.Description;
        var result = await expenses.UpdateAsync(
            context.User.Id,
            expenseId,
            categoryId ?? item.CategoryId,
            amount ?? item.Amount,
            newDescription,
            date ?? item.ExpenseDate,
            Today(context),
            cancellationToken);

        if (!result.Saved)
        {
            var key = result.Status switch
            {
                ExpenseChangeStatus.InvalidAmount => MessageKeys.AmountInvalid,
                ExpenseChangeStatus.InvalidDate => MessageKeys.DateFuture,
                _ => MessageKeys.ExpenseNotFound,
            };

            return await BuildListAsync(context, [Said(context, key)], cancellationToken);
        }

        var notices = new List<BotResponse> { Said(context, MessageKeys.ExpenseUpdated) };
        notices.AddRange(BudgetAlertMessages.Render(
            messages, moneyFormatter, context.Language, context.User.Currency, result.Alerts));

        return await BuildDetailAsync(context, expenseId, notices, cancellationToken);
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

    private string DateLabel(ConversationContext context, DateOnly date) =>
        $"{date.Day} de {messages.Get(context.Language, MessageKeys.Months[date.Month - 1])} de {date.Year}";

    private BotResponse Said(ConversationContext context, string key, params object?[] args) =>
        BotResponse.Message(messages.Get(context.Language, key, args));
}
