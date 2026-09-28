using Microsoft.EntityFrameworkCore;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Infrastructure.Persistence.Records;

namespace MyBudget.Infrastructure.Persistence.Repositories;

internal sealed class ConversationStore(MyBudgetDbContext dbContext, TimeProvider timeProvider)
    : IConversationStore
{
    public async Task<ConversationSnapshot?> FindAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var record = await dbContext.ConversationStates
            .AsNoTracking()
            .FirstOrDefaultAsync(state => state.UserId == userId, cancellationToken);

        if (record is null)
        {
            return null;
        }

        if (record.ExpiresAt <= timeProvider.GetUtcNow())
        {
            // An abandoned flow is cleared rather than resumed: resuming a stale conversation
            // after days would be more confusing than starting again.
            await ClearAsync(userId, cancellationToken);
            return null;
        }

        return new ConversationSnapshot(
            record.UserId,
            record.ChatId,
            record.Conversation,
            record.State,
            record.Payload,
            record.ExpiresAt);
    }

    public async Task SaveAsync(
        ConversationSnapshot conversation, CancellationToken cancellationToken = default)
    {
        var existing = await dbContext.ConversationStates
            .FirstOrDefaultAsync(state => state.UserId == conversation.UserId, cancellationToken);

        var now = timeProvider.GetUtcNow();

        if (existing is null)
        {
            dbContext.ConversationStates.Add(new ConversationStateRecord
            {
                UserId = conversation.UserId,
                ChatId = conversation.ChatId,
                Conversation = conversation.Conversation,
                State = conversation.State,
                Payload = conversation.Payload,
                ExpiresAt = conversation.ExpiresAt,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else
        {
            existing.ChatId = conversation.ChatId;
            existing.Conversation = conversation.Conversation;
            existing.State = conversation.State;
            existing.Payload = conversation.Payload;
            existing.ExpiresAt = conversation.ExpiresAt;
            existing.UpdatedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearAsync(Guid userId, CancellationToken cancellationToken = default) =>
        await dbContext.ConversationStates
            .Where(state => state.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
}
