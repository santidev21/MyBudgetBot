using MyBudget.Application.Money;

namespace MyBudget.Telegram.Conversations;

/// <summary>
/// The entry point of the expense flow for a free-text message.
/// <para>
/// Compact entry is the same flow with the amount and description already known, so the
/// conversation owns the decision rather than the router: parsed text goes straight to the
/// category picker, a bare amount asks for the description, and genuine ambiguity falls back
/// to asking for the amount.
/// </para>
/// </summary>
internal interface IExpenseEntry
{
    Task<ConversationTurn> StartFromCompactAsync(
        ConversationContext context, CompactExpenseResult parsed, CancellationToken cancellationToken);
}
