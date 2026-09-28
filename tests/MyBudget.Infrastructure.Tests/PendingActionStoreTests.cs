using FluentAssertions;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

/// <summary>
/// The consume-once guarantee against real PostgreSQL.
/// <para>
/// A replayed callback and a forged callback are the two things this store exists to defeat, so
/// both are tested here: claiming twice returns nothing the second time, and claiming somebody
/// else's draft returns nothing at all.
/// </para>
/// </summary>
public sealed class PendingActionStoreTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task A_draft_can_be_consumed_once_and_carries_its_payload()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new PendingActionStore(context, new StubTimeProvider(Now));
        var action = NewAction(user.Id, Now.AddMinutes(30));
        await store.CreateAsync(action);

        var consumed = await store.ConsumeAsync(user.Id, action.Id);

        consumed.Should().NotBeNull();
        consumed!.Action.Should().Be("expense");
        consumed.Payload.Should().Contain("35000");
    }

    [Fact]
    public async Task Claiming_the_same_draft_twice_returns_nothing_the_second_time()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new PendingActionStore(context, new StubTimeProvider(Now));
        var action = NewAction(user.Id, Now.AddMinutes(30));
        await store.CreateAsync(action);

        (await store.ConsumeAsync(user.Id, action.Id)).Should().NotBeNull();
        (await store.ConsumeAsync(user.Id, action.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Another_users_draft_cannot_be_consumed()
    {
        await using var context = CreateContext();
        var owner = TestData.NewUser(1);
        var stranger = TestData.NewUser(2);
        context.Users.AddRange(owner, stranger);
        await context.SaveChangesAsync();

        var store = new PendingActionStore(context, new StubTimeProvider(Now));
        var action = NewAction(owner.Id, Now.AddMinutes(30));
        await store.CreateAsync(action);

        (await store.ConsumeAsync(stranger.Id, action.Id)).Should().BeNull();

        // The failed attempt must not have consumed it for the real owner.
        (await store.ConsumeAsync(owner.Id, action.Id)).Should().NotBeNull();
    }

    [Fact]
    public async Task An_expired_draft_cannot_be_consumed()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new PendingActionStore(context, new StubTimeProvider(Now));
        var action = NewAction(user.Id, Now.AddMinutes(-1));
        await store.CreateAsync(action);

        (await store.ConsumeAsync(user.Id, action.Id)).Should().BeNull();
    }

    [Fact]
    public async Task An_unknown_draft_cannot_be_consumed()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new PendingActionStore(context, new StubTimeProvider(Now));

        (await store.ConsumeAsync(user.Id, Guid.NewGuid())).Should().BeNull();
    }

    [Fact]
    public async Task Deleting_a_user_takes_their_drafts_with_them()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new PendingActionStore(context, new StubTimeProvider(Now));
        await store.CreateAsync(NewAction(user.Id, Now.AddMinutes(30)));

        await new UserDataEraser(context).EraseAsync(user.Id);

        await using var verification = CreateContext();
        verification.PendingActions.Should().BeEmpty();
    }

    private static PendingAction NewAction(Guid userId, DateTimeOffset expiresAt) =>
        new(Guid.NewGuid(), userId, "expense", "{\"amount\":35000}", expiresAt);

    private sealed class StubTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
