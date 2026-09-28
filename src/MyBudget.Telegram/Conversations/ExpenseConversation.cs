using Microsoft.Extensions.Options;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Application.Categories;
using MyBudget.Application.Dates;
using MyBudget.Application.Expenses;
using MyBudget.Application.Localization;
using MyBudget.Application.Matching;
using MyBudget.Application.Money;
using MyBudget.Domain.Categories;
using MyBudget.Domain.Expenses;
using MyBudget.Telegram.Options;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// Guided expense entry: amount, description, category, date, confirmation.
/// <para>
/// The confirmation is the only place a draft can become an expense. The draft is stored
/// server side as a pending action and claimed exactly once, so a double tap or a replayed
/// callback registers one expense, never two. The callback carries only the pending action id.
/// </para>
/// </summary>
internal sealed class ExpenseConversation(
    IUserMessages messages,
    IExpenseService expenses,
    ICategoryService categories,
    IPendingActionStore pendingActions,
    IMoneyParser moneyParser,
    IMoneyFormatter moneyFormatter,
    IDateParser dateParser,
    ICategoryMatcher matcher,
    IUserLocalDate localDate,
    TimeProvider timeProvider,
    IOptions<TelegramOptions> options,
    MainMenu menu) : IConversation, IExpenseEntry
{
    public const string ConversationName = "expense";

    private const string CallbackPrefix = "exp:";
    private const string SkipCallback = CallbackPrefix + "skip";
    private const string CancelCallback = CallbackPrefix + "cancel";
    private const string CategoryPrefix = CallbackPrefix + "cat:";
    private const string ChangeCategoryCallback = CallbackPrefix + "category";
    private const string ChangeDateCallback = CallbackPrefix + "date";
    private const string TodayCallback = CallbackPrefix + "today";
    private const string YesterdayCallback = CallbackPrefix + "yesterday";
    private const string LearnSaveCallback = CallbackPrefix + "learn:save";
    private const string LearnConfirmCallback = CallbackPrefix + "learn:confirm";
    private const string LearnSkipCallback = CallbackPrefix + "learn:skip";
    private const string ConfirmPrefix = "v1|expense|";

    private const string AwaitingAmountState = "awaiting-amount";
    private const string AwaitingDescriptionState = "awaiting-description";
    private const string CategoryState = "category";
    private const string AwaitingDateState = "awaiting-date";
    private const string LearnKeywordState = "learn-keyword";
    private const string ConfirmState = "confirm";

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(
        ConversationContext context, CancellationToken cancellationToken) =>
        Task.FromResult(PromptAmount(context));

    public async Task<ConversationTurn> StartFromCompactAsync(
        ConversationContext context, CompactExpenseResult parsed, CancellationToken cancellationToken)
    {
        switch (parsed.Outcome)
        {
            case CompactExpenseOutcome.Parsed:
                // Both halves are known; the matcher decides the category or asks for it.
                return await AfterDescriptionAsync(
                    context,
                    new ExpensePayload { Amount = parsed.Amount, Description = parsed.Description },
                    cancellationToken);

            case CompactExpenseOutcome.AmountOnly:
                // A bare number answers "how much?" and asks for the description next.
                return DescriptionPrompt(context, new ExpensePayload { Amount = parsed.Amount });

            case CompactExpenseOutcome.Ambiguous:
                // Two plausible amounts: ask rather than guess.
                return PromptAmount(context, Said(context, MessageKeys.ExpenseAmbiguousAmount));

            default:
                return PromptAmount(context);
        }
    }

    public async Task<ConversationTurn> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken)
    {
        var payload = ExpensePayload.Parse(context.Conversation?.Payload);

        return context.CurrentState switch
        {
            AwaitingDescriptionState => await HandleDescriptionAsync(context, payload, text.Text, cancellationToken),
            AwaitingDateState => await HandleDateAsync(context, payload, text.Text, cancellationToken),
            _ => await HandleAmountAsync(context, payload, text.Text, cancellationToken),
        };
    }

    public async Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        var data = callback.Data;
        var payload = ExpensePayload.Parse(context.Conversation?.Payload);

        if (data == CancelCallback)
        {
            return Cancelled(context);
        }

        if (data == SkipCallback)
        {
            return await CategoryPickerAsync(
                context,
                payload with { Description = null, Source = CategorizationSource.Manual, LearnTerm = null },
                [],
                cancellationToken);
        }

        if (data == ChangeCategoryCallback)
        {
            return await CategoryPickerAsync(
                context, payload with { Source = CategorizationSource.Manual, LearnTerm = null }, [],
                cancellationToken);
        }

        if (data == LearnSaveCallback)
        {
            return await LearnKeywordAsync(context, payload, allowConflict: false, cancellationToken);
        }

        if (data == LearnConfirmCallback)
        {
            return await LearnKeywordAsync(context, payload, allowConflict: true, cancellationToken);
        }

        if (data == LearnSkipCallback)
        {
            return await ConfirmationAsync(
                context, payload with { LearnTerm = null }, [], cancellationToken);
        }

        if (data == ChangeDateCallback)
        {
            return DatePrompt(context, payload, []);
        }

        if (data == TodayCallback)
        {
            return await ConfirmationAsync(
                context, payload with { Date = Today(context) }, [], cancellationToken);
        }

        if (data == YesterdayCallback)
        {
            return await ConfirmationAsync(
                context, payload with { Date = Today(context).AddDays(-1) }, [], cancellationToken);
        }

        if (data.StartsWith(CategoryPrefix, StringComparison.Ordinal)
            && Guid.TryParse(data[CategoryPrefix.Length..], out var chosenId))
        {
            return await ChooseCategoryAsync(context, payload, chosenId, cancellationToken);
        }

        if (data.StartsWith(ConfirmPrefix, StringComparison.Ordinal)
            && Guid.TryParse(data[ConfirmPrefix.Length..], out var pendingId))
        {
            return await ConfirmAsync(context, pendingId, cancellationToken);
        }

        return PromptAmount(context);
    }

    private Task<ConversationTurn> HandleAmountAsync(
        ConversationContext context, ExpensePayload payload, string raw, CancellationToken cancellationToken)
    {
        var parsed = moneyParser.Parse(raw, context.User.Currency);

        if (parsed is MoneyParseResult.Success amount)
        {
            return Task.FromResult(DescriptionPrompt(context, payload with { Amount = amount.Amount }));
        }

        var key = parsed is MoneyParseResult.Invalid invalid
            ? InputErrorMessages.ForAmount(invalid.Reason)
            : MessageKeys.AmountInvalid;

        return Task.FromResult(PromptAmount(context, Said(context, key)));
    }

    private async Task<ConversationTurn> HandleDescriptionAsync(
        ConversationContext context, ExpensePayload payload, string raw, CancellationToken cancellationToken)
    {
        var description = string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
        return await AfterDescriptionAsync(context, payload with { Description = description }, cancellationToken);
    }

    private async Task<ConversationTurn> HandleDateAsync(
        ConversationContext context, ExpensePayload payload, string raw, CancellationToken cancellationToken)
    {
        var parsed = dateParser.Parse(raw, Today(context));

        if (parsed is DateParseResult.Success success)
        {
            return await ConfirmationAsync(context, payload with { Date = success.Date }, [], cancellationToken);
        }

        var key = parsed is DateParseResult.Invalid invalid
            ? InputErrorMessages.ForDate(invalid.Reason)
            : MessageKeys.DateInvalid;

        return DatePrompt(context, payload, [Said(context, key)]);
    }

    /// <summary>
    /// Runs the matcher once the description is known and routes to a suggestion, an ambiguous
    /// picker, or the picker plus the offer to learn the term (design §9).
    /// </summary>
    private async Task<ConversationTurn> AfterDescriptionAsync(
        ConversationContext context, ExpensePayload payload, CancellationToken cancellationToken)
    {
        var active = await categories.ListAsync(context.User.Id, includeInactive: false, cancellationToken);
        if (active.Count == 0)
        {
            return NoCategories(context);
        }

        var description = payload.Description;
        if (string.IsNullOrWhiteSpace(description))
        {
            return BuildPickerTurn(
                context, payload with { Source = CategorizationSource.Manual, LearnTerm = null }, active, []);
        }

        var inputs = active
            .Select(category => new CategoryMatchInput(
                category.Id, category.Name, category.Icon, category.Aliases.Select(alias => alias.Alias).ToList()))
            .ToList();

        var match = matcher.Match(description, inputs);

        switch (match.Outcome)
        {
            case CategoryMatchOutcome.Matched when match.Suggestion is { } suggestion:
                return await ConfirmationAsync(
                    context,
                    payload with
                    {
                        CategoryId = suggestion.CategoryId,
                        CategoryName = suggestion.Name,
                        CategoryIcon = suggestion.Icon,
                        Source = CategorizationSource.Matched,
                        LearnTerm = null,
                    },
                    [Said(context, MessageKeys.ExpenseCategorySuggestion, $"{suggestion.Icon} {suggestion.Name}")],
                    cancellationToken);

            case CategoryMatchOutcome.Ambiguous:
                return BuildPickerTurn(
                    context,
                    payload with { Source = CategorizationSource.Ambiguous, LearnTerm = null },
                    active,
                    [Said(context, MessageKeys.ExpenseCategoryAmbiguous)]);

            default:
                // Learning only makes sense for a real term the user typed.
                return BuildPickerTurn(
                    context,
                    payload with { Source = CategorizationSource.Manual, LearnTerm = description },
                    active,
                    [Said(context, MessageKeys.ExpenseCategoryNone)]);
        }
    }

    private async Task<ConversationTurn> ChooseCategoryAsync(
        ConversationContext context, ExpensePayload payload, Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await categories.GetAsync(context.User.Id, categoryId, cancellationToken);
        if (category is null)
        {
            return await CategoryPickerAsync(
                context, payload, [Said(context, MessageKeys.ExpenseNotFound)], cancellationToken);
        }

        var next = payload with
        {
            CategoryId = category.Id,
            CategoryName = category.Name,
            CategoryIcon = category.Icon,
            Source = payload.Source ?? CategorizationSource.Manual,
        };

        return next.LearnTerm is { Length: > 0 }
            ? LearnPromptTurn(context, next)
            : await ConfirmationAsync(context, next, [], cancellationToken);
    }

    /// <summary>
    /// Offers to save the unrecognized term as a keyword of the chosen category, showing exactly
    /// what will be stored before anything is written.
    /// </summary>
    private ConversationTurn LearnPromptTurn(ConversationContext context, ExpensePayload payload)
    {
        var language = context.Language;
        var label = $"{payload.CategoryIcon} {payload.CategoryName}";
        var keyboard = BotKeyboard.Inline(
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonSaveAlias), LearnSaveCallback),
                new BotButton(messages.Get(language, MessageKeys.ButtonSkip), LearnSkipCallback),
            },
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback),
            });

        return new ConversationTurn(
        [
            BotResponse.Message(
                messages.Get(language, MessageKeys.ExpenseLearnKeywordPrompt, payload.LearnTerm, label),
                keyboard),
        ])
        {
            NextState = LearnKeywordState,
            NextPayload = payload.Serialize(),
        };
    }

    private async Task<ConversationTurn> LearnKeywordAsync(
        ConversationContext context,
        ExpensePayload payload,
        bool allowConflict,
        CancellationToken cancellationToken)
    {
        if (payload.CategoryId is not { } categoryId || payload.LearnTerm is not { Length: > 0 } term)
        {
            return await ConfirmationAsync(
                context, payload with { LearnTerm = null }, [], cancellationToken);
        }

        var language = context.Language;
        var label = $"{payload.CategoryIcon} {payload.CategoryName}";
        var result = await categories.AddAliasAsync(
            context.User.Id, categoryId, term, allowConflict, cancellationToken);

        switch (result.Status)
        {
            case AliasChangeStatus.Added:
                return await ConfirmationAsync(
                    context,
                    payload with { LearnTerm = null },
                    [Said(context, MessageKeys.ExpenseLearnKeywordSaved, term, label)],
                    cancellationToken);

            case AliasChangeStatus.Duplicate:
                // Already a keyword of this category; nothing to store and nothing to warn about.
                return await ConfirmationAsync(
                    context,
                    payload with { LearnTerm = null },
                    [Said(context, MessageKeys.AliasDuplicate)],
                    cancellationToken);

            case AliasChangeStatus.Conflict:
                var owners = string.Join(", ", result.ConflictingCategories.Select(owner => owner.Name));
                var keyboard = BotKeyboard.Inline(
                    new[]
                    {
                        new BotButton(
                            messages.Get(language, MessageKeys.AliasButtonAddAnyway), LearnConfirmCallback),
                        new BotButton(messages.Get(language, MessageKeys.ButtonSkip), LearnSkipCallback),
                    },
                    new[]
                    {
                        new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback),
                    });

                return new ConversationTurn(
                [
                    Said(context, MessageKeys.AliasConflict, owners),
                    BotResponse.Message(messages.Get(language, MessageKeys.AliasConflictHint), keyboard),
                ])
                {
                    NextState = LearnKeywordState,
                    NextPayload = payload.Serialize(),
                };

            default:
                return await CategoryPickerAsync(
                    context,
                    payload with { LearnTerm = null },
                    [Said(context, MessageKeys.CategoryNotFound)],
                    cancellationToken);
        }
    }

    private async Task<ConversationTurn> ConfirmAsync(
        ConversationContext context, Guid pendingId, CancellationToken cancellationToken)
    {
        var claimed = await pendingActions.ConsumeAsync(context.User.Id, pendingId, cancellationToken);
        if (claimed is null)
        {
            return Expired(context);
        }

        var draft = ExpensePayload.Parse(claimed.Payload);
        if (draft.Amount is not { } amount || draft.CategoryId is not { } categoryId)
        {
            return Expired(context);
        }

        var today = Today(context);
        var result = await expenses.CreateAsync(
            context.User.Id, categoryId, amount, draft.Description, draft.Date ?? today, today,
            draft.Source ?? CategorizationSource.Manual, cancellationToken);

        if (!result.Saved || result.Expense is null)
        {
            return new ConversationTurn(
            [
                BotResponse.Message(
                    messages.Get(context.Language, MessageKeys.UnexpectedError),
                    menu.ReplyKeyboard(context.Language)),
            ])
            {
                Completed = true,
            };
        }

        var undo = BotKeyboard.Inline(new[]
        {
            new BotButton(
                messages.Get(context.Language, MessageKeys.ButtonUndo),
                ExpenseUndoHandler.Prefix + result.Expense.Id),
        });

        return new ConversationTurn(
        [
            BotResponse.Message(messages.Get(context.Language, MessageKeys.ExpenseRegistered), undo),
        ])
        {
            Completed = true,
        };
    }

    private ConversationTurn PromptAmount(ConversationContext context, params BotResponse[] notices)
    {
        var keyboard = BotKeyboard.Inline(new[]
        {
            new BotButton(messages.Get(context.Language, MessageKeys.ButtonCancel), CancelCallback),
        });

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(messages.Get(context.Language, MessageKeys.AmountPrompt), keyboard),
        };

        return new ConversationTurn(responses)
        {
            NextState = AwaitingAmountState,
            NextPayload = new ExpensePayload().Serialize(),
        };
    }

    private ConversationTurn DescriptionPrompt(ConversationContext context, ExpensePayload payload)
    {
        var keyboard = BotKeyboard.Inline(
            new[]
            {
                new BotButton(messages.Get(context.Language, MessageKeys.ButtonSkip), SkipCallback),
            },
            new[]
            {
                new BotButton(messages.Get(context.Language, MessageKeys.ButtonCancel), CancelCallback),
            });

        return new ConversationTurn(
        [
            BotResponse.Message(
                messages.Get(context.Language, MessageKeys.ExpenseDescriptionPrompt),
                keyboard),
        ])
        {
            NextState = AwaitingDescriptionState,
            NextPayload = payload.Serialize(),
        };
    }

    private async Task<ConversationTurn> CategoryPickerAsync(
        ConversationContext context,
        ExpensePayload payload,
        IReadOnlyList<BotResponse> notices,
        CancellationToken cancellationToken)
    {
        var active = await categories.ListAsync(context.User.Id, includeInactive: false, cancellationToken);
        return active.Count == 0
            ? NoCategories(context)
            : BuildPickerTurn(context, payload, active, notices);
    }

    private ConversationTurn BuildPickerTurn(
        ConversationContext context,
        ExpensePayload payload,
        IReadOnlyList<BudgetCategory> active,
        IReadOnlyList<BotResponse> notices)
    {
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

    private ConversationTurn NoCategories(ConversationContext context) =>
        new([BotResponse.Message(
            messages.Get(context.Language, MessageKeys.ExpenseNoCategories),
            menu.ReplyKeyboard(context.Language))])
        {
            Completed = true,
        };

    private ConversationTurn DatePrompt(
        ConversationContext context, ExpensePayload payload, IReadOnlyList<BotResponse> notices)
    {
        var language = context.Language;
        var keyboard = BotKeyboard.Inline(
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonToday), TodayCallback),
                new BotButton(messages.Get(language, MessageKeys.ButtonYesterday), YesterdayCallback),
            },
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback),
            });

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(messages.Get(language, MessageKeys.DatePrompt), keyboard),
        };

        return new ConversationTurn(responses)
        {
            NextState = AwaitingDateState,
            NextPayload = payload.Serialize(),
        };
    }

    private async Task<ConversationTurn> ConfirmationAsync(
        ConversationContext context,
        ExpensePayload payload,
        IReadOnlyList<BotResponse> notices,
        CancellationToken cancellationToken)
    {
        var language = context.Language;
        var draft = payload with { Date = payload.Date ?? Today(context) };

        var pendingId = Guid.NewGuid();
        await pendingActions.CreateAsync(
            new PendingAction(
                pendingId,
                context.User.Id,
                "expense",
                draft.Serialize(),
                timeProvider.GetUtcNow() + options.Value.ConversationTimeout),
            cancellationToken);

        var keyboard = BotKeyboard.Inline(
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonConfirm), ConfirmPrefix + pendingId),
                new BotButton(messages.Get(language, MessageKeys.ButtonChangeCategory), ChangeCategoryCallback),
            },
            new[]
            {
                new BotButton(messages.Get(language, MessageKeys.ButtonChangeDate), ChangeDateCallback),
                new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback),
            });

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(ConfirmationText(context, draft), keyboard),
        };

        return new ConversationTurn(responses)
        {
            NextState = ConfirmState,
            NextPayload = draft.Serialize(),
        };
    }

    private string ConfirmationText(ConversationContext context, ExpensePayload draft)
    {
        var language = context.Language;
        var description = string.IsNullOrWhiteSpace(draft.Description)
            ? messages.Get(language, MessageKeys.ExpenseNoDescription)
            : draft.Description!;

        return string.Join(
            "\n",
            messages.Get(language, MessageKeys.ExpenseConfirmationHeader),
            messages.Get(
                language,
                MessageKeys.ExpenseConfirmationAmount,
                moneyFormatter.Format(draft.Amount ?? 0, context.User.Currency)),
            messages.Get(language, MessageKeys.ExpenseConfirmationDescription, description),
            messages.Get(
                language,
                MessageKeys.ExpenseConfirmationCategory,
                $"{draft.CategoryIcon} {draft.CategoryName}"),
            messages.Get(
                language,
                MessageKeys.ExpenseConfirmationDate,
                DateLabel(context, draft.Date ?? Today(context))));
    }

    private ConversationTurn Expired(ConversationContext context) =>
        new([BotResponse.Message(
            messages.Get(context.Language, MessageKeys.ExpenseExpired),
            menu.ReplyKeyboard(context.Language))])
        {
            Completed = true,
        };

    private ConversationTurn Cancelled(ConversationContext context) =>
        new([BotResponse.Message(
            messages.Get(context.Language, MessageKeys.Cancelled),
            menu.ReplyKeyboard(context.Language))])
        {
            Completed = true,
        };

    private DateOnly Today(ConversationContext context) => localDate.Today(context.User.TimeZone);

    private string DateLabel(ConversationContext context, DateOnly date) =>
        $"{date.Day} de {messages.Get(context.Language, MessageKeys.Months[date.Month - 1])} de {date.Year}";

    private BotResponse Said(ConversationContext context, string key, params object?[] args) =>
        BotResponse.Message(messages.Get(context.Language, key, args));
}
