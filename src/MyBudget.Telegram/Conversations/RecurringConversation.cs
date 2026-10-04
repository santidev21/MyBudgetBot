using System.Globalization;
using MyBudget.Application.Categories;
using MyBudget.Application.Dates;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Application.Recurring;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Recurring;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// Recurring-rule management: list, create, pause, resume and delete.
/// <para>
/// Rules are configuration, not history. The flow never touches an already-registered expense,
/// and the scheduler is what turns a due rule into a real expense.
/// </para>
/// </summary>
internal sealed class RecurringConversation(
    IUserMessages messages,
    IRecurringExpenseService recurring,
    ICategoryService categories,
    IMoneyParser moneyParser,
    IMoneyFormatter moneyFormatter,
    IUserLocalDate localDate,
    MainMenu menu) : IConversation
{
    public const string ConversationName = "recurring";

    private const string CallbackPrefix = "rec|";
    private const string NewCallback = CallbackPrefix + "new";
    private const string BackCallback = CallbackPrefix + "back";
    private const string SkipCallback = CallbackPrefix + "skip";
    private const string SaveCallback = CallbackPrefix + "save";
    private const string CancelCallback = CallbackPrefix + "cancel";
    private const string OpenPrefix = CallbackPrefix + "open|";
    private const string PausePrefix = CallbackPrefix + "pause|";
    private const string ResumePrefix = CallbackPrefix + "resume|";
    private const string DeletePrefix = CallbackPrefix + "delete|";
    private const string DeleteConfirmPrefix = CallbackPrefix + "delete-confirm|";
    private const string CategoryPrefix = CallbackPrefix + "cat|";

    private const string ListState = "list";
    private const string DetailState = "detail";
    private const string DeleteState = "delete-confirm";
    private const string AmountState = "new-amount";
    private const string DescriptionState = "new-description";
    private const string CategoryState = "new-category";
    private const string DayState = "new-day";
    private const string ConfirmState = "new-confirm";

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(
        ConversationContext context, CancellationToken cancellationToken) =>
        ListTurnAsync(context, [], cancellationToken);

    public async Task<ConversationTurn?> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken)
    {
        var payload = RecurringPayload.Parse(context.Conversation?.Payload);

        // Only the creation states wait for typed values; the list and the detail are button-driven.
        return context.CurrentState switch
        {
            AmountState => HandleAmount(context, payload, text.Text),
            DescriptionState => await HandleDescriptionAsync(context, payload, text.Text, cancellationToken),
            DayState => HandleDay(context, payload, text.Text),
            _ => null,
        };
    }

    public async Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        var data = callback.Data;
        var payload = RecurringPayload.Parse(context.Conversation?.Payload);

        if (data == CancelCallback)
        {
            return Cancelled(context);
        }

        if (data == NewCallback)
        {
            return AmountPrompt(context, new RecurringPayload(), []);
        }

        if (data == BackCallback)
        {
            return await ListTurnAsync(context, [], cancellationToken);
        }

        if (data == SkipCallback)
        {
            return await CategoryPickerAsync(
                context, payload with { Description = null }, [], cancellationToken);
        }

        if (data == SaveCallback)
        {
            return await SaveAsync(context, payload, cancellationToken);
        }

        if (TryReadGuid(data, OpenPrefix, out var openId))
        {
            return await DetailTurnAsync(context, openId, [], cancellationToken);
        }

        if (TryReadGuid(data, PausePrefix, out var pauseId))
        {
            return await SetActiveAsync(context, pauseId, active: false, cancellationToken);
        }

        if (TryReadGuid(data, ResumePrefix, out var resumeId))
        {
            return await SetActiveAsync(context, resumeId, active: true, cancellationToken);
        }

        if (TryReadGuid(data, DeleteConfirmPrefix, out var deleteId))
        {
            return await DeleteAsync(context, deleteId, cancellationToken);
        }

        if (TryReadGuid(data, DeletePrefix, out var confirmId))
        {
            return await DeleteConfirmTurnAsync(context, confirmId, cancellationToken);
        }

        if (TryReadGuid(data, CategoryPrefix, out var chosenId))
        {
            return await ChooseCategoryAsync(context, payload, chosenId, cancellationToken);
        }

        return await ListTurnAsync(context, [], cancellationToken);
    }

    private ConversationTurn HandleAmount(ConversationContext context, RecurringPayload payload, string raw)
    {
        var parsed = moneyParser.Parse(raw, context.User.Currency);

        if (parsed is MoneyParseResult.Success amount)
        {
            return DescriptionPrompt(context, payload with { Amount = amount.Amount });
        }

        var key = parsed is MoneyParseResult.Invalid invalid
            ? InputErrorMessages.ForAmount(invalid.Reason)
            : MessageKeys.AmountInvalid;

        return AmountPrompt(context, payload, [Said(context, key)]);
    }

    private async Task<ConversationTurn> HandleDescriptionAsync(
        ConversationContext context, RecurringPayload payload, string raw, CancellationToken cancellationToken)
    {
        var description = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        return await CategoryPickerAsync(context, payload with { Description = description }, [], cancellationToken);
    }

    private ConversationTurn HandleDay(ConversationContext context, RecurringPayload payload, string raw)
    {
        var valid = int.TryParse(
                        raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var day)
                    && day is >= RecurringExpense.MinDayOfMonth and <= RecurringExpense.MaxDayOfMonth;

        return valid
            ? Confirmation(context, payload with { DayOfMonth = day })
            : DayPrompt(context, payload, [Said(context, MessageKeys.RecurringDayInvalid)]);
    }

    private async Task<ConversationTurn> SaveAsync(
        ConversationContext context, RecurringPayload payload, CancellationToken cancellationToken)
    {
        if (payload.Amount is not { } amount
            || payload.CategoryId is not { } categoryId
            || payload.DayOfMonth is not { } day)
        {
            return AmountPrompt(context, new RecurringPayload(), [Said(context, MessageKeys.UnexpectedError)]);
        }

        // The rule starts today: a day already past this month first falls due next month.
        var result = await recurring.CreateAsync(
            context.User.Id, categoryId, amount, payload.Description, day, Today(context),
            endDate: null, cancellationToken);

        return result.Status switch
        {
            RecurringChangeStatus.Saved => await ListTurnAsync(
                context, [Said(context, MessageKeys.RecurringCreated)], cancellationToken),
            RecurringChangeStatus.CategoryNotFound => await ListTurnAsync(
                context, [Said(context, MessageKeys.CategoryNotFound)], cancellationToken),
            _ => await ListTurnAsync(
                context, [Said(context, MessageKeys.UnexpectedError)], cancellationToken),
        };
    }

    private async Task<ConversationTurn> ChooseCategoryAsync(
        ConversationContext context, RecurringPayload payload, Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await categories.GetAsync(context.User.Id, categoryId, cancellationToken);
        if (category is null)
        {
            return await CategoryPickerAsync(
                context,
                payload with { CategoryId = null, CategoryName = null, CategoryIcon = null },
                [Said(context, MessageKeys.CategoryNotFound)],
                cancellationToken);
        }

        return DayPrompt(context, payload with
        {
            CategoryId = category.Id,
            CategoryName = category.Name,
            CategoryIcon = category.Icon,
        }, []);
    }

    private async Task<ConversationTurn> SetActiveAsync(
        ConversationContext context, Guid ruleId, bool active, CancellationToken cancellationToken)
    {
        var result = await recurring.SetActiveAsync(context.User.Id, ruleId, active, cancellationToken);
        if (!result.Saved)
        {
            return await ListTurnAsync(context, [Said(context, MessageKeys.RecurringNotFound)], cancellationToken);
        }

        var notice = active ? MessageKeys.RecurringResumed : MessageKeys.RecurringPaused;
        return await DetailTurnAsync(context, ruleId, [Said(context, notice)], cancellationToken);
    }

    private async Task<ConversationTurn> DeleteConfirmTurnAsync(
        ConversationContext context, Guid ruleId, CancellationToken cancellationToken)
    {
        var rule = await recurring.GetViewAsync(context.User.Id, ruleId, cancellationToken);
        if (rule is null)
        {
            return await ListTurnAsync(context, [Said(context, MessageKeys.RecurringNotFound)], cancellationToken);
        }

        var language = context.Language;
        var keyboard = BotKeyboard.Inline(
            new[]
            {
                new BotButton(
                    messages.Get(language, MessageKeys.RecurringButtonDeleteConfirm),
                    DeleteConfirmPrefix + ruleId),
            },
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.RecurringButtonBack), BackCallback),
            });

        return new ConversationTurn(
        [
            BotResponse.Message(
                messages.Get(language, MessageKeys.RecurringDeleteConfirm, Label(context, rule)), keyboard),
        ])
        {
            NextState = DeleteState,
            NextPayload = new RecurringPayload().WithRule(ruleId).Serialize(),
        };
    }

    private async Task<ConversationTurn> DeleteAsync(
        ConversationContext context, Guid ruleId, CancellationToken cancellationToken)
    {
        var deleted = await recurring.DeleteAsync(context.User.Id, ruleId, cancellationToken);
        var key = deleted ? MessageKeys.RecurringDeleted : MessageKeys.RecurringNotFound;

        return await ListTurnAsync(context, [Said(context, key)], cancellationToken);
    }

    private async Task<ConversationTurn> ListTurnAsync(
        ConversationContext context, IReadOnlyList<BotResponse> notices, CancellationToken cancellationToken)
    {
        var language = context.Language;
        var rules = await recurring.ListAsync(context.User.Id, cancellationToken);
        var responses = new List<BotResponse>(notices);

        if (rules.Count == 0)
        {
            responses.Add(BotResponse.Message(
                messages.Get(language, MessageKeys.RecurringEmpty),
                BotKeyboard.Inline(new[]
                {
                    new BotButton(messages.Get(language, MessageKeys.RecurringButtonNew), NewCallback),
                })));
        }
        else
        {
            var lines = new List<string> { messages.Get(language, MessageKeys.RecurringListHeader) };
            lines.AddRange(rules.Select(rule => LineText(context, rule)));

            var rows = rules
                .Select(rule => new[] { new BotButton(RuleButton(context, rule), OpenPrefix + rule.Id) })
                .ToList();
            rows.Add([new BotButton(messages.Get(language, MessageKeys.RecurringButtonNew), NewCallback)]);

            responses.Add(BotResponse.Message(string.Join("\n", lines), BotKeyboard.Inline([.. rows])));
        }

        return new ConversationTurn(responses)
        {
            NextState = ListState,
            NextPayload = new RecurringPayload().Serialize(),
        };
    }

    private async Task<ConversationTurn> DetailTurnAsync(
        ConversationContext context,
        Guid ruleId,
        IReadOnlyList<BotResponse> notices,
        CancellationToken cancellationToken)
    {
        var rule = await recurring.GetViewAsync(context.User.Id, ruleId, cancellationToken);
        if (rule is null)
        {
            return await ListTurnAsync(context, [Said(context, MessageKeys.RecurringNotFound)], cancellationToken);
        }

        var language = context.Language;
        var activateButton = rule.IsActive
            ? new BotButton(messages.Get(language, MessageKeys.RecurringButtonPause), PausePrefix + rule.Id)
            : new BotButton(messages.Get(language, MessageKeys.RecurringButtonResume), ResumePrefix + rule.Id);

        var keyboard = BotKeyboard.Inline(
            [activateButton, new BotButton(messages.Get(language, MessageKeys.RecurringButtonDelete), DeletePrefix + rule.Id)],
            [new BotButton(messages.Get(language, MessageKeys.RecurringButtonBack), BackCallback)]);

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(DetailText(context, rule), keyboard),
        };

        return new ConversationTurn(responses)
        {
            NextState = DetailState,
            NextPayload = new RecurringPayload().WithRule(rule.Id).Serialize(),
        };
    }

    private string DetailText(ConversationContext context, RecurringExpenseView rule)
    {
        var language = context.Language;
        var lines = new List<string>
        {
            messages.Get(language, MessageKeys.RecurringDetailHeader, Label(context, rule)),
            messages.Get(
                language,
                MessageKeys.ExpenseConfirmationAmount,
                moneyFormatter.Format(rule.Amount, context.User.Currency)),
        };

        if (!string.IsNullOrWhiteSpace(rule.Description))
        {
            lines.Add(messages.Get(language, MessageKeys.ExpenseConfirmationDescription, rule.Description));
        }

        lines.Add(messages.Get(
            language, MessageKeys.ExpenseConfirmationCategory, $"{rule.Icon} {rule.CategoryName}"));
        lines.Add(messages.Get(language, MessageKeys.RecurringDayLine, rule.DayOfMonth));
        lines.Add(messages.Get(
            language,
            MessageKeys.RecurringDetailStatus,
            messages.Get(
                language,
                rule.IsActive ? MessageKeys.RecurringStatusActive : MessageKeys.RecurringStatusPaused)));

        if (rule.LastGeneratedDate is { } lastGenerated)
        {
            lines.Add(messages.Get(
                language, MessageKeys.RecurringDetailLastGenerated, DateLabel(context, lastGenerated)));
        }

        return string.Join("\n", lines);
    }

    private ConversationTurn AmountPrompt(
        ConversationContext context, RecurringPayload payload, IReadOnlyList<BotResponse> notices)
        => Step(
            context,
            MessageKeys.RecurringAmountPrompt,
            AmountState,
            payload,
            notices,
            skip: false);

    private ConversationTurn DescriptionPrompt(ConversationContext context, RecurringPayload payload)
        => Step(
            context,
            MessageKeys.RecurringDescriptionPrompt,
            DescriptionState,
            payload,
            [],
            skip: true);

    private ConversationTurn DayPrompt(
        ConversationContext context, RecurringPayload payload, IReadOnlyList<BotResponse> notices)
        => Step(
            context,
            MessageKeys.RecurringDayPrompt,
            DayState,
            payload,
            notices,
            skip: false);

    private ConversationTurn Step(
        ConversationContext context,
        string promptKey,
        string state,
        RecurringPayload payload,
        IReadOnlyList<BotResponse> notices,
        bool skip)
    {
        var language = context.Language;
        var buttons = new List<BotButton>();
        var rows = new List<BotButton[]>();

        if (skip)
        {
            buttons.Add(new BotButton(messages.Get(language, MessageKeys.ButtonSkip), SkipCallback));
        }

        buttons.Add(new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback));
        rows.Add([.. buttons]);

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(messages.Get(language, promptKey), BotKeyboard.Inline([.. rows])),
        };

        return new ConversationTurn(responses)
        {
            NextState = state,
            NextPayload = payload.Serialize(),
        };
    }

    private async Task<ConversationTurn> CategoryPickerAsync(
        ConversationContext context,
        RecurringPayload payload,
        IReadOnlyList<BotResponse> notices,
        CancellationToken cancellationToken)
    {
        var active = await categories.ListAsync(context.User.Id, includeInactive: false, cancellationToken);
        if (active.Count == 0)
        {
            return new ConversationTurn(
            [
                BotResponse.Message(
                    messages.Get(context.Language, MessageKeys.ExpenseNoCategories),
                    menu.ReplyKeyboard(context.Language)),
            ])
            {
                Completed = true,
            };
        }

        var language = context.Language;
        var rows = active
            .Select(category => new[]
            {
                new BotButton($"{category.Icon} {category.Name}", CategoryPrefix + category.Id),
            })
            .ToList();
        rows.Add([new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback)]);

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(
                messages.Get(language, MessageKeys.ExpenseCategoryPrompt),
                BotKeyboard.Inline([.. rows])),
        };

        return new ConversationTurn(responses)
        {
            NextState = CategoryState,
            NextPayload = payload.Serialize(),
        };
    }

    private ConversationTurn Confirmation(ConversationContext context, RecurringPayload payload)
    {
        var language = context.Language;
        var description = string.IsNullOrWhiteSpace(payload.Description)
            ? messages.Get(language, MessageKeys.ExpenseNoDescription)
            : payload.Description!;

        var text = string.Join(
            "\n",
            messages.Get(language, MessageKeys.RecurringConfirmHeader),
            messages.Get(
                language,
                MessageKeys.ExpenseConfirmationAmount,
                moneyFormatter.Format(payload.Amount ?? 0, context.User.Currency)),
            messages.Get(language, MessageKeys.ExpenseConfirmationDescription, description),
            messages.Get(
                language,
                MessageKeys.ExpenseConfirmationCategory,
                $"{payload.CategoryIcon} {payload.CategoryName}"),
            messages.Get(language, MessageKeys.RecurringDayLine, payload.DayOfMonth),
            messages.Get(language, MessageKeys.RecurringConfirmNote));

        var keyboard = BotKeyboard.Inline(
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonConfirm), SaveCallback),
            },
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback),
            });

        return new ConversationTurn([BotResponse.Message(text, keyboard)])
        {
            NextState = ConfirmState,
            NextPayload = payload.Serialize(),
        };
    }

    private ConversationTurn Cancelled(ConversationContext context) =>
        new([BotResponse.Message(
            messages.Get(context.Language, MessageKeys.Cancelled),
            menu.ReplyKeyboard(context.Language))])
        {
            Completed = true,
        };

    /// <summary>The icon plus the description, falling back to the category name.</summary>
    private static string Label(ConversationContext context, RecurringExpenseView rule) =>
        $"{rule.Icon} {(string.IsNullOrWhiteSpace(rule.Description) ? rule.CategoryName : rule.Description)}";

    private string LineText(ConversationContext context, RecurringExpenseView rule)
    {
        var key = rule.IsActive ? MessageKeys.RecurringLine : MessageKeys.RecurringInactiveLine;
        return messages.Get(
            context.Language,
            key,
            Label(context, rule),
            moneyFormatter.Format(rule.Amount, context.User.Currency),
            rule.DayOfMonth);
    }

    private string RuleButton(ConversationContext context, RecurringExpenseView rule)
    {
        if (rule.IsActive)
        {
            return Label(context, rule);
        }

        return $"{Label(context, rule)} · " +
               messages.Get(context.Language, MessageKeys.RecurringStatusPaused);
    }

    private static bool TryReadGuid(string data, string prefix, out Guid id)
    {
        if (data.StartsWith(prefix, StringComparison.Ordinal)
            && Guid.TryParse(data[prefix.Length..], out id))
        {
            return true;
        }

        id = Guid.Empty;
        return false;
    }

    private DateOnly Today(ConversationContext context) => localDate.Today(context.User.TimeZone);

    private string DateLabel(ConversationContext context, DateOnly date) =>
        $"{date.Day} de {messages.Get(context.Language, MessageKeys.Months[date.Month - 1])} de {date.Year}";

    private BotResponse Said(ConversationContext context, string key, params object?[] args) =>
        BotResponse.Message(messages.Get(context.Language, key, args));
}
