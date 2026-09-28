using MyBudget.Telegram.Tests.Fakes;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// Builds the exact shapes Telegram sends, so the dispatcher is exercised against the same
/// types it will see in production.
/// </summary>
internal static class TestUpdates
{
    private const long DefaultChatId = 4242;

    /// <summary>Defaults to the harness clock so the staleness gate is deterministic.</summary>
    private static DateTime FreshTimestamp => TestClock.Now.UtcDateTime;

    public static Update PrivateMessage(
        int updateId,
        long userId,
        string? text,
        long chatId = DefaultChatId,
        DateTime? sentAt = null,
        ChatType chatType = ChatType.Private)
    {
        var from = new User { Id = userId, FirstName = "Test", IsBot = false };

        var message = new Message
        {
            Id = updateId,
            Date = sentAt ?? FreshTimestamp,
            Chat = new Chat { Id = chatId, Type = chatType },
            From = from,
            Text = text,
        };

        return new Update { Id = updateId, Message = message };
    }

    public static Update Callback(
        int updateId,
        long userId,
        string data,
        long chatId = DefaultChatId,
        DateTime? sentAt = null)
    {
        var from = new User { Id = userId, FirstName = "Test", IsBot = false };

        var message = new Message
        {
            Id = updateId,
            Date = sentAt ?? FreshTimestamp,
            Chat = new Chat { Id = chatId, Type = ChatType.Private },
            From = from,
        };

        return new Update
        {
            Id = updateId,
            CallbackQuery = new CallbackQuery
            {
                Id = $"callback-{updateId}",
                From = from,
                Data = data,
                Message = message,
            },
        };
    }

    public static Update Unsupported(int updateId, long userId)
    {
        var from = new User { Id = userId, FirstName = "Test", IsBot = false };

        return new Update
        {
            Id = updateId,
            CallbackQuery = new CallbackQuery
            {
                Id = $"callback-{updateId}",
                From = from,
                Data = "noop",
                Message = null,
            },
        };
    }
}
