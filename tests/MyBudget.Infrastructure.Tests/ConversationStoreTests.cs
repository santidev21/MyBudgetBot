using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using MyBudget.Application.Abstractions.Telegram;
using MyBudget.Infrastructure.Persistence.Repositories;

namespace MyBudget.Infrastructure.Tests;

public sealed class ConversationStoreTests(DatabaseFixture fixture) : DatabaseTestBase(fixture)
{
    [Fact]
    public async Task A_conversation_round_trips_with_its_payload()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        var store = new ConversationStore(context, time);

        await store.SaveAsync(new ConversationSnapshot(
            user.Id, 555, "start", "awaiting-timezone", """{"step":1}""", time.GetUtcNow().AddMinutes(30)));

        var found = await store.FindAsync(user.Id);

        found.Should().NotBeNull();
        found!.Conversation.Should().Be("start");
        found.State.Should().Be("awaiting-timezone");
        found.ChatId.Should().Be(555);

        // jsonb canonicalises the document (whitespace, key order), so the payload is compared
        // as data. It is structured state, never a string to match on.
        using var payload = JsonDocument.Parse(found.Payload);
        payload.RootElement.GetProperty("step").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Saving_again_replaces_the_single_row_per_user()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new ConversationStore(context, TimeProvider.System);

        await store.SaveAsync(new ConversationSnapshot(
            user.Id, 555, "start", "one", "{}", DateTimeOffset.UtcNow.AddMinutes(30)));
        await store.SaveAsync(new ConversationSnapshot(
            user.Id, 555, "start", "two", "{}", DateTimeOffset.UtcNow.AddMinutes(30)));

        var found = await store.FindAsync(user.Id);

        found!.State.Should().Be("two");
        context.ConversationStates.Should().ContainSingle();
    }

    [Fact]
    public async Task An_expired_conversation_is_cleared_rather_than_resumed()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var time = new FakeTimeProvider(new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero));
        var store = new ConversationStore(context, time);

        await store.SaveAsync(new ConversationSnapshot(
            user.Id, 555, "start", "awaiting-timezone", "{}", time.GetUtcNow().AddMinutes(30)));

        time.Advance(TimeSpan.FromMinutes(31));

        (await store.FindAsync(user.Id)).Should().BeNull();
        context.ConversationStates.Should().BeEmpty();
    }

    [Fact]
    public async Task Clearing_removes_the_state()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new ConversationStore(context, TimeProvider.System);
        await store.SaveAsync(new ConversationSnapshot(
            user.Id, 555, "start", "one", "{}", DateTimeOffset.UtcNow.AddMinutes(30)));

        await store.ClearAsync(user.Id);

        (await store.FindAsync(user.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Deleting_a_user_removes_their_conversation()
    {
        await using var context = CreateContext();
        var user = TestData.NewUser();
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var store = new ConversationStore(context, TimeProvider.System);
        await store.SaveAsync(new ConversationSnapshot(
            user.Id, 555, "start", "one", "{}", DateTimeOffset.UtcNow.AddMinutes(30)));

        await new UserDataEraser(context).EraseAsync(user.Id);

        await using var verification = CreateContext();
        verification.ConversationStates.Should().BeEmpty();
    }
}
