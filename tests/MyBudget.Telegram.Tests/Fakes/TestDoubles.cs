using MyBudget.Application.Abstractions.Persistence;
using MyBudget.Application.Abstractions.Telegram;

namespace MyBudget.Telegram.Tests.Fakes;

/// <summary>The inbox, in memory. Mirrors the real claim semantics so the pipeline can be tested.</summary>
internal sealed class InMemoryUpdateInbox : IUpdateInbox
{
    private readonly Dictionary<long, string> _statuses = [];
    private readonly HashSet<long> _claims = [];

    public int ClaimCount { get; private set; }

    public Task<bool> TryClaimAsync(long updateId, CancellationToken cancellationToken = default)
    {
        ClaimCount++;

        if (_statuses.TryGetValue(updateId, out var status) && status != "failed")
        {
            return Task.FromResult(false);
        }

        _statuses[updateId] = "received";
        _claims.Add(updateId);
        return Task.FromResult(true);
    }

    public Task CompleteAsync(long updateId, Guid? userId, CancellationToken cancellationToken = default)
    {
        _statuses[updateId] = "processed";
        return Task.CompletedTask;
    }

    public Task FailAsync(long updateId, string reason, CancellationToken cancellationToken = default)
    {
        _statuses[updateId] = "failed";
        return Task.CompletedTask;
    }

    public Task IgnoreAsync(
        long updateId, Guid? userId, string reason, CancellationToken cancellationToken = default)
    {
        _statuses[updateId] = "ignored";
        return Task.CompletedTask;
    }

    public Task<int> PurgeOlderThanAsync(
        DateTimeOffset cutoff, CancellationToken cancellationToken = default) => Task.FromResult(0);

    public string? StatusOf(long updateId) => _statuses.GetValueOrDefault(updateId);

    public bool WasClaimed(long updateId) => _claims.Contains(updateId);
}

internal sealed class InMemoryConversationStore : IConversationStore
{
    private readonly Dictionary<Guid, ConversationSnapshot> _snapshots = [];

    public Task<ConversationSnapshot?> FindAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshots.GetValueOrDefault(userId));

    public Task SaveAsync(ConversationSnapshot conversation, CancellationToken cancellationToken = default)
    {
        _snapshots[conversation.UserId] = conversation;
        return Task.CompletedTask;
    }

    public Task ClearAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        _snapshots.Remove(userId);
        return Task.CompletedTask;
    }

    public ConversationSnapshot? SnapshotOf(Guid userId) => _snapshots.GetValueOrDefault(userId);

    public int Count => _snapshots.Count;
}

/// <summary>
/// Runs the work immediately. Serialisation itself is proven against PostgreSQL in the
/// infrastructure tests; here the pipeline is what matters.
/// </summary>
internal sealed class PassThroughUserWorkLock : IUserWorkLock
{
    public Task<TResult> ExecuteAsync<TResult>(
        Guid userId,
        Func<CancellationToken, Task<TResult>> work,
        CancellationToken cancellationToken = default) => work(cancellationToken);
}

internal sealed class RecordingTelegramSender : Presentation.ITelegramSender
{
    public List<(long ChatId, string Text)> Messages { get; } = [];

    public List<string> AcknowledgedCallbacks { get; } = [];

    public Task SendAsync(
        long chatId, IReadOnlyList<Presentation.BotResponse> responses,
        CancellationToken cancellationToken = default)
    {
        foreach (var response in responses)
        {
            Messages.Add((chatId, response.Text));
        }

        return Task.CompletedTask;
    }

    public Task AcknowledgeCallbackAsync(
        string callbackQueryId, string? notification = null, CancellationToken cancellationToken = default)
    {
        AcknowledgedCallbacks.Add(callbackQueryId);
        return Task.CompletedTask;
    }
}

/// <summary>Counts writes so a test can prove a conversation persisted its own state.</summary>
internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public List<object> Detached { get; } = [];

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SaveCount++;
        return Task.FromResult(1);
    }

    public void Detach(object entity) => Detached.Add(entity);
}

internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public static FixedClock At(DateTimeOffset now) => new(now);
}

internal static class TestClock
{
    /// <summary>A fixed instant, so the staleness gate and persisted expiries are deterministic.</summary>
    public static readonly DateTimeOffset Now = new(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);
}
