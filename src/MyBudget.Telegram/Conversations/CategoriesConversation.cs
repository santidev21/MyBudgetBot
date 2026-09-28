using MyBudget.Application.Budgets;
using MyBudget.Application.Categories;
using MyBudget.Application.Dates;
using MyBudget.Application.Localization;
using MyBudget.Application.Money;
using MyBudget.Domain.Categories;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// Category management: list, create, rename, change the icon, activate and deactivate.
/// <para>
/// Deletion is deliberately not offered. A category with expenses or a funded allocation cannot
/// be deleted, so the flow offers deactivation, and reusing the name of a deactivated category
/// reactivates it with its history intact. Budget and alias screens are added in later slices of
/// the same flow.
/// </para>
/// </summary>
internal sealed partial class CategoriesConversation(
    IUserMessages messages,
    ICategoryService categories,
    IBudgetService budgets,
    IMoneyParser moneyParser,
    IMoneyFormatter moneyFormatter,
    IUserLocalDate localDate,
    MainMenu menu) : IConversation
{
    public const string ConversationName = "categories";

    private const string CallbackPrefix = "cats:";
    private const string ListCallback = CallbackPrefix + "list";
    private const string DetailCallback = CallbackPrefix + "detail";
    private const string NewCallback = CallbackPrefix + "new";
    private const string OpenPrefix = CallbackPrefix + "open:";
    private const string RenameCallback = CallbackPrefix + "rename";
    private const string IconCallback = CallbackPrefix + "icon";
    private const string IconSkipCallback = CallbackPrefix + "icon-skip";
    private const string ToggleCallback = CallbackPrefix + "toggle";
    private const string CancelCallback = CallbackPrefix + "cancel";

    private const string MenuState = "menu";
    private const string DetailState = "detail";
    private const string AwaitingNameState = "awaiting-name";
    private const string AwaitingIconState = "awaiting-icon";
    private const string AwaitingRenameState = "awaiting-rename";
    private const string AwaitingIconChangeState = "awaiting-icon-change";

    public string Name => ConversationName;

    public Task<ConversationTurn> StartAsync(
        ConversationContext context, CancellationToken cancellationToken) =>
        BuildListAsync(context, cancellationToken, []);

    public async Task<ConversationTurn> HandleTextAsync(
        ConversationContext context, IncomingText text, CancellationToken cancellationToken)
    {
        var payload = CategoriesPayload.Parse(context.Conversation?.Payload);

        return context.CurrentState switch
        {
            AwaitingNameState => await HandleNameAsync(context, text.Text, cancellationToken),
            AwaitingIconState => await HandleIconAsync(
                context, payload.Name, text.Text, cancellationToken),
            AwaitingRenameState => await HandleRenameAsync(
                context, payload.CategoryId, text.Text, cancellationToken),
            AwaitingIconChangeState => await HandleIconChangeAsync(
                context, payload.CategoryId, text.Text, cancellationToken),
            AwaitingAliasState => await HandleAliasTextAsync(
                context, payload, text.Text, cancellationToken),
            AwaitingBudgetAmountState => await HandleBudgetAmountAsync(
                context, payload, text.Text, cancellationToken),
            _ => await BuildListAsync(context, cancellationToken, []),
        };
    }

    public async Task<ConversationTurn> HandleCallbackAsync(
        ConversationContext context, IncomingCallback callback, CancellationToken cancellationToken)
    {
        var data = callback.Data;
        var payload = CategoriesPayload.Parse(context.Conversation?.Payload);

        if (data == CancelCallback)
        {
            return Cancelled(context);
        }

        if (data == NewCallback)
        {
            return Prompt(
                context, AwaitingNameState, new CategoriesPayload(),
                MessageKeys.CategoryNamePrompt, allowSkip: false, backCallback: ListCallback);
        }

        if (data == ListCallback)
        {
            return await BuildListAsync(context, cancellationToken, []);
        }

        if (data == DetailCallback)
        {
            return payload.CategoryId is { } detailId
                ? await BuildDetailAsync(context, detailId, cancellationToken, [])
                : await BuildListAsync(context, cancellationToken, []);
        }

        if (data == RenameCallback)
        {
            return payload.CategoryId is { } renameId
                ? Prompt(
                    context, AwaitingRenameState, payload,
                    MessageKeys.CategoryRenamePrompt, allowSkip: false, backCallback: DetailCallback)
                : await BuildListAsync(context, cancellationToken, []);
        }

        if (data == IconCallback)
        {
            return payload.CategoryId is not null
                ? Prompt(
                    context, AwaitingIconChangeState, payload,
                    MessageKeys.CategoryIconChangePrompt, allowSkip: false, backCallback: DetailCallback)
                : await BuildListAsync(context, cancellationToken, []);
        }

        if (data == IconSkipCallback)
        {
            // Skipping the icon is only meaningful while a new category is being created.
            return context.CurrentState == AwaitingIconState && payload.Name is not null
                ? await HandleIconAsync(context, payload.Name, null, cancellationToken)
                : await BuildListAsync(context, cancellationToken, []);
        }

        if (data == ToggleCallback)
        {
            return payload.CategoryId is { } toggleId
                ? await ToggleAsync(context, toggleId, cancellationToken)
                : await BuildListAsync(context, cancellationToken, []);
        }

        if (data.StartsWith(OpenPrefix, StringComparison.Ordinal)
            && Guid.TryParse(data[OpenPrefix.Length..], out var openId))
        {
            return await BuildDetailAsync(context, openId, cancellationToken, []);
        }

        if (await HandleAliasCallbackAsync(context, data, payload, cancellationToken) is { } aliasTurn)
        {
            return aliasTurn;
        }

        if (await HandleBudgetCallbackAsync(context, data, payload, cancellationToken) is { } budgetTurn)
        {
            return budgetTurn;
        }

        return await BuildListAsync(context, cancellationToken, []);
    }

    private async Task<ConversationTurn> BuildListAsync(
        ConversationContext context,
        CancellationToken cancellationToken,
        IReadOnlyList<BotResponse> notices)
    {
        var language = context.Language;
        var all = await categories.ListAsync(context.User.Id, includeInactive: true, cancellationToken);

        var headerKey = all.Count == 0 ? MessageKeys.CategoryListEmpty : MessageKeys.CategoryListHeader;

        var rows = all
            .Select(category => new[] { new BotButton(Label(language, category), OpenPrefix + category.Id) })
            .ToList();
        rows.Add([new BotButton(messages.Get(language, MessageKeys.CategoryButtonNew), NewCallback)]);
        rows.Add([new BotButton(messages.Get(language, MessageKeys.CategoryButtonBudget), BudgetCallback)]);

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(
                messages.Get(language, headerKey),
                BotKeyboard.Inline([.. rows])),
        };

        return new ConversationTurn(responses)
        {
            NextState = MenuState,
            NextPayload = new CategoriesPayload().Serialize(),
        };
    }

    private async Task<ConversationTurn> BuildDetailAsync(
        ConversationContext context,
        Guid categoryId,
        CancellationToken cancellationToken,
        IReadOnlyList<BotResponse> notices)
    {
        var category = await categories.GetAsync(context.User.Id, categoryId, cancellationToken);
        if (category is null)
        {
            return await BuildListAsync(
                context, cancellationToken,
                [.. notices, Said(context, MessageKeys.CategoryNotFound)]);
        }

        var language = context.Language;
        var header = messages.Get(language, MessageKeys.CategoryDetailHeader, category.Icon, category.Name);
        if (!category.IsActive)
        {
            header += "\n" + messages.Get(language, MessageKeys.CategoryStatusInactive);
        }

        var toggleKey = category.IsActive
            ? MessageKeys.CategoryButtonDeactivate
            : MessageKeys.CategoryButtonActivate;

        var keyboard = BotKeyboard.Inline(
        [
            [
                new BotButton(messages.Get(language, MessageKeys.CategoryButtonRename), RenameCallback),
                new BotButton(messages.Get(language, MessageKeys.CategoryButtonIcon), IconCallback),
            ],
            [new BotButton(messages.Get(language, MessageKeys.CategoryButtonAliases), AliasesCallback)],
            [new BotButton(messages.Get(language, toggleKey), ToggleCallback)],
            [new BotButton(messages.Get(language, MessageKeys.CategoryButtonBack), ListCallback)],
        ]);

        var responses = new List<BotResponse>(notices) { BotResponse.Message(header, keyboard) };

        return new ConversationTurn(responses)
        {
            NextState = DetailState,
            NextPayload = new CategoriesPayload().WithCategory(categoryId).Serialize(),
        };
    }

    private Task<ConversationTurn> HandleNameAsync(
        ConversationContext context, string rawName, CancellationToken cancellationToken)
    {
        var language = context.Language;
        var name = rawName.Trim();

        if (name.Length == 0 || name.Length > BudgetCategory.MaxNameLength)
        {
            return Task.FromResult(Prompt(
                context, AwaitingNameState, new CategoriesPayload(),
                MessageKeys.CategoryNamePrompt, allowSkip: false, backCallback: ListCallback,
                Said(context, MessageKeys.CategoryNameInvalid, BudgetCategory.MaxNameLength)));
        }

        return Task.FromResult(Prompt(
            context, AwaitingIconState, new CategoriesPayload { Name = name },
            MessageKeys.CategoryIconPrompt, allowSkip: true, backCallback: NewCallback));
    }

    private async Task<ConversationTurn> HandleIconAsync(
        ConversationContext context, string? name, string? rawIcon, CancellationToken cancellationToken)
    {
        if (name is null)
        {
            return await BuildListAsync(context, cancellationToken, []);
        }

        var icon = rawIcon?.Trim();
        if (icon is not null && icon.Length > BudgetCategory.MaxIconLength)
        {
            return Prompt(
                context, AwaitingIconState, new CategoriesPayload { Name = name },
                MessageKeys.CategoryIconPrompt, allowSkip: true, backCallback: NewCallback,
                Said(context, MessageKeys.CategoryIconInvalid, BudgetCategory.MaxIconLength));
        }

        var result = await categories.CreateAsync(
            context.User.Id, name, string.IsNullOrEmpty(icon) ? null : icon, cancellationToken);

        return result.Status switch
        {
            CategoryChangeStatus.Saved => await BuildListAsync(
                context, cancellationToken, [Said(context, MessageKeys.CategoryCreated)]),
            CategoryChangeStatus.Reactivated => await BuildListAsync(
                context, cancellationToken, [Said(context, MessageKeys.CategoryReactivated)]),
            CategoryChangeStatus.NameTaken => Prompt(
                context, AwaitingNameState, new CategoriesPayload(),
                MessageKeys.CategoryNamePrompt, allowSkip: false, backCallback: ListCallback,
                Said(context, MessageKeys.CategoryNameTaken)),
            _ => await BuildListAsync(context, cancellationToken, []),
        };
    }

    private async Task<ConversationTurn> HandleRenameAsync(
        ConversationContext context, Guid? categoryId, string rawName, CancellationToken cancellationToken)
    {
        if (categoryId is not { } id)
        {
            return await BuildListAsync(context, cancellationToken, []);
        }

        var name = rawName.Trim();
        if (name.Length == 0 || name.Length > BudgetCategory.MaxNameLength)
        {
            return Prompt(
                context, AwaitingRenameState, new CategoriesPayload().WithCategory(id),
                MessageKeys.CategoryRenamePrompt, allowSkip: false, backCallback: DetailCallback,
                Said(context, MessageKeys.CategoryNameInvalid, BudgetCategory.MaxNameLength));
        }

        var result = await categories.RenameAsync(context.User.Id, id, name, cancellationToken);

        return result.Status switch
        {
            CategoryChangeStatus.Saved => await BuildDetailAsync(
                context, id, cancellationToken, [Said(context, MessageKeys.CategoryRenamed)]),
            CategoryChangeStatus.NameTaken => Prompt(
                context, AwaitingRenameState, new CategoriesPayload().WithCategory(id),
                MessageKeys.CategoryRenamePrompt, allowSkip: false, backCallback: DetailCallback,
                Said(context, MessageKeys.CategoryNameTaken)),
            _ => await BuildListAsync(
                context, cancellationToken, [Said(context, MessageKeys.CategoryNotFound)]),
        };
    }

    private async Task<ConversationTurn> HandleIconChangeAsync(
        ConversationContext context, Guid? categoryId, string rawIcon, CancellationToken cancellationToken)
    {
        if (categoryId is not { } id)
        {
            return await BuildListAsync(context, cancellationToken, []);
        }

        var icon = rawIcon.Trim();
        if (icon.Length > BudgetCategory.MaxIconLength)
        {
            return Prompt(
                context, AwaitingIconChangeState, new CategoriesPayload().WithCategory(id),
                MessageKeys.CategoryIconChangePrompt, allowSkip: false, backCallback: DetailCallback,
                Said(context, MessageKeys.CategoryIconInvalid, BudgetCategory.MaxIconLength));
        }

        var result = await categories.ChangeIconAsync(
            context.User.Id, id, string.IsNullOrEmpty(icon) ? null : icon, cancellationToken);

        return result.Status == CategoryChangeStatus.Saved
            ? await BuildDetailAsync(
                context, id, cancellationToken, [Said(context, MessageKeys.CategoryIconChanged)])
            : await BuildListAsync(
                context, cancellationToken, [Said(context, MessageKeys.CategoryNotFound)]);
    }

    private async Task<ConversationTurn> ToggleAsync(
        ConversationContext context, Guid categoryId, CancellationToken cancellationToken)
    {
        var category = await categories.GetAsync(context.User.Id, categoryId, cancellationToken);
        if (category is null)
        {
            return await BuildListAsync(
                context, cancellationToken, [Said(context, MessageKeys.CategoryNotFound)]);
        }

        var isActive = !category.IsActive;
        await categories.SetActiveAsync(context.User.Id, categoryId, isActive, cancellationToken);

        var noticeKey = isActive ? MessageKeys.CategoryActivated : MessageKeys.CategoryDeactivated;
        return await BuildDetailAsync(context, categoryId, cancellationToken, [Said(context, noticeKey)]);
    }

    private ConversationTurn Prompt(
        ConversationContext context,
        string state,
        CategoriesPayload payload,
        string promptKey,
        bool allowSkip,
        string? backCallback,
        params BotResponse[] notices) =>
        PromptTurn(context, state, payload, promptKey, allowSkip, backCallback, notices, []);

    private ConversationTurn PromptTurn(
        ConversationContext context,
        string state,
        CategoriesPayload payload,
        string promptKey,
        bool allowSkip,
        string? backCallback,
        IReadOnlyList<BotResponse> notices,
        object?[] promptArgs)
    {
        var language = context.Language;
        var rows = new List<BotButton[]>();

        if (allowSkip)
        {
            rows.Add([new BotButton(
                messages.Get(language, MessageKeys.CategoryButtonSkip), IconSkipCallback)]);
        }

        // Every prompt offers a way back to the step it came from, so a mistap never forces the
        // user to leave the whole flow: Cancelar closes it, Volver steps back one screen.
        var navigation = new List<BotButton>();
        if (backCallback is not null)
        {
            navigation.Add(new BotButton(
                messages.Get(language, MessageKeys.CategoryButtonBack), backCallback));
        }

        navigation.Add(new BotButton(messages.Get(language, MessageKeys.ButtonCancel), CancelCallback));
        rows.Add([.. navigation]);

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(
                messages.Get(language, promptKey, promptArgs),
                BotKeyboard.Inline([.. rows])),
        };

        return new ConversationTurn(responses)
        {
            NextState = state,
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

    private string Label(string language, BudgetCategory category)
    {
        var prefix = category.IsActive
            ? string.Empty
            : messages.Get(language, MessageKeys.CategoryInactiveMarker) + " ";

        return $"{prefix}{category.Icon} {category.Name}";
    }

    private BotResponse Said(ConversationContext context, string key, params object?[] args) =>
        BotResponse.Message(messages.Get(context.Language, key, args));
}
