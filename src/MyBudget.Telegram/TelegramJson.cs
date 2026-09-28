using System.Text.Json;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace MyBudget.Telegram;

/// <summary>
/// Deserialization of incoming updates.
/// <para>
/// Telegram's payload is polymorphic, so the SDK's own options are used rather than a plain
/// <c>JsonSerializer</c>. A hand-rolled reader would silently mistype a field the day Telegram
/// adds one.
/// </para>
/// </summary>
public static class TelegramJson
{
    public static Update? TryDeserializeUpdate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Update>(json, JsonBotAPI.Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
