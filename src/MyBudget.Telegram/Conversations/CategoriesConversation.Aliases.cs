using MyBudget.Application.Categories;
using MyBudget.Application.Localization;
using MyBudget.Domain.Categories;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The alias slice of the category flow: list the keywords of a category, add one, remove one,
/// and resolve the conflict prompt when the term already belongs to another category.
/// <para>
/// Duplicate keywords across categories are allowed on purpose, because that is how genuine
/// ambiguity is modelled. The conflict is therefore a prompt, never a refusal: adding anyway
/// records the ambiguity, and the matcher will ask the user which category is meant.
/// </para>
/// </summary>
internal sealed partial class CategoriesConversation
{
    private const string AliasesCallback = CallbackPrefix + "aliases";
    private const string AliasAddCallback = CallbackPrefix + "alias:add";
    private const string AliasConfirmCallback = CallbackPrefix + "alias:confirm";
    private const string AliasCancelCallback = CallbackPrefix + "alias:cancel";
    private const string AliasBackCallback = CallbackPrefix + "alias:back";
    private const string AliasRemovePrefix = CallbackPrefix + "alias:rm:";

    private const string AliasScreenState = "aliases";
    private const string AwaitingAliasState = "awaiting-alias";
    private const string AliasConflictState = "alias-conflict";

    private async Task<ConversationTurn> BuildAliasScreenAsync(
        ConversationContext context,
        Guid? categoryId,
        CancellationToken cancellationToken,
        IReadOnlyList<BotResponse> notices)
    {
        if (categoryId is not { } id)
        {
            return await BuildListAsync(context, cancellationToken, []);
        }

        var category = await categories.GetAsync(context.User.Id, id, cancellationToken);
        if (category is null)
        {
            return await BuildListAsync(
                context, cancellationToken, [Said(context, MessageKeys.CategoryNotFound)]);
        }

        var language = context.Language;
        var header = messages.Get(language, MessageKeys.AliasListHeader, $"{category.Icon} {category.Name}");
        if (category.Aliases.Count == 0)
        {
            header += "\n" + messages.Get(language, MessageKeys.AliasListEmpty);
        }

        var removePrefix = messages.Get(language, MessageKeys.AliasButtonRemovePrefix);
        var rows = category.Aliases
            .Select((alias, index) => new[]
            {
                new BotButton($"{removePrefix} {alias.Alias}", AliasRemovePrefix + index),
            })
            .ToList();
        rows.Add([new BotButton(messages.Get(language, MessageKeys.AliasButtonAdd), AliasAddCallback)]);
        rows.Add([new BotButton(messages.Get(language, MessageKeys.CategoryButtonBack), AliasBackCallback)]);

        var responses = new List<BotResponse>(notices)
        {
            BotResponse.Message(header, BotKeyboard.Inline([.. rows])),
        };

        return new ConversationTurn(responses)
        {
            NextState = AliasScreenState,
            NextPayload = new CategoriesPayload
            {
                CategoryId = id,
                Aliases = category.Aliases.Select(alias => alias.Alias).ToList(),
            }.Serialize(),
        };
    }

    private async Task<ConversationTurn> HandleAliasTextAsync(
        ConversationContext context, CategoriesPayload payload, string rawAlias,
        CancellationToken cancellationToken)
    {
        if (payload.CategoryId is not { } id)
        {
            return await BuildListAsync(context, cancellationToken, []);
        }

        var alias = rawAlias.Trim();
        if (alias.Length == 0 || alias.Length > BudgetCategory.MaxAliasLength)
        {
            return Prompt(
                context, AwaitingAliasState, payload,
                MessageKeys.AliasPrompt, allowSkip: false, backCallback: AliasesCallback,
                Said(context, MessageKeys.AliasInvalid, BudgetCategory.MaxAliasLength));
        }

        var result = await categories.AddAliasAsync(
            context.User.Id, id, alias, allowConflict: false, cancellationToken);

        return await AliasResultTurnAsync(context, id, alias, result, cancellationToken);
    }

    private async Task<ConversationTurn?> HandleAliasCallbackAsync(
        ConversationContext context, string data, CategoriesPayload payload,
        CancellationToken cancellationToken)
    {
        if (data == AliasesCallback)
        {
            return await BuildAliasScreenAsync(context, payload.CategoryId, cancellationToken, []);
        }

        if (data == AliasAddCallback)
        {
            return payload.CategoryId is null
                ? await BuildListAsync(context, cancellationToken, [])
                : Prompt(
                    context, AwaitingAliasState, payload,
                    MessageKeys.AliasPrompt, allowSkip: false, backCallback: AliasesCallback);
        }

        if (data == AliasBackCallback)
        {
            return payload.CategoryId is { } backId
                ? await BuildDetailAsync(context, backId, cancellationToken, [])
                : await BuildListAsync(context, cancellationToken, []);
        }

        if (data == AliasCancelCallback)
        {
            return await BuildAliasScreenAsync(context, payload.CategoryId, cancellationToken, []);
        }

        if (data == AliasConfirmCallback)
        {
            if (payload.CategoryId is not { } confirmId || payload.Alias is not { } alias)
            {
                return await BuildListAsync(context, cancellationToken, []);
            }

            var confirmed = await categories.AddAliasAsync(
                context.User.Id, confirmId, alias, allowConflict: true, cancellationToken);

            return await AliasResultTurnAsync(context, confirmId, alias, confirmed, cancellationToken);
        }

        if (data.StartsWith(AliasRemovePrefix, StringComparison.Ordinal)
            && int.TryParse(data[AliasRemovePrefix.Length..], out var index)
            && payload.CategoryId is { } removeId
            && payload.Aliases is { } aliases
            && index >= 0
            && index < aliases.Count)
        {
            var removed = await categories.RemoveAliasAsync(
                context.User.Id, removeId, aliases[index], cancellationToken);

            return await BuildAliasScreenAsync(
                context, removeId, cancellationToken,
                removed ? [Said(context, MessageKeys.AliasRemoved)] : []);
        }

        return null;
    }

    private async Task<ConversationTurn> AliasResultTurnAsync(
        ConversationContext context, Guid categoryId, string alias, AliasChangeResult result,
        CancellationToken cancellationToken)
    {
        switch (result.Status)
        {
            case AliasChangeStatus.Added:
                return await BuildAliasScreenAsync(
                    context, categoryId, cancellationToken, [Said(context, MessageKeys.AliasAdded)]);

            case AliasChangeStatus.Duplicate:
                return await BuildAliasScreenAsync(
                    context, categoryId, cancellationToken, [Said(context, MessageKeys.AliasDuplicate)]);

            case AliasChangeStatus.Conflict:
                var owners = string.Join(", ", result.ConflictingCategories.Select(owner => owner.Name));
                var keyboard = BotKeyboard.Inline(
                [
                    [
                        new BotButton(
                            messages.Get(context.Language, MessageKeys.AliasButtonAddAnyway),
                            AliasConfirmCallback),
                    ],
                    [
                        new BotButton(
                            messages.Get(context.Language, MessageKeys.ButtonCancel),
                            AliasCancelCallback),
                    ],
                ]);

                return new ConversationTurn(
                [
                    Said(context, MessageKeys.AliasConflict, owners),
                    BotResponse.Message(messages.Get(context.Language, MessageKeys.AliasConflictHint), keyboard),
                ])
                {
                    NextState = AliasConflictState,
                    NextPayload = new CategoriesPayload { CategoryId = categoryId, Alias = alias }.Serialize(),
                };

            default:
                return await BuildListAsync(
                    context, cancellationToken, [Said(context, MessageKeys.CategoryNotFound)]);
        }
    }
}
