using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using MyBudget.Telegram;
using MyBudget.Telegram.Options;

namespace MyBudget.Api.Endpoints;

/// <summary>
/// The Telegram webhook.
/// <para>
/// Two independent gates, both compared in constant time: an unguessable path segment and the
/// secret header Telegram sends. A rejection answers 404 so the endpoint's existence is not
/// confirmed. The request body is never logged: it contains the user's own message text.
/// </para>
/// </summary>
internal static class TelegramWebhookEndpoint
{
    private const string SecretHeaderName = "X-Telegram-Bot-Api-Secret-Token";

    public static void MapTelegramWebhook(this WebApplication app)
    {
        app.MapPost($"{TelegramWebhook.RoutePrefix}/{{webhookPath}}", HandleAsync);
    }

    private static async Task<IResult> HandleAsync(
        HttpContext httpContext,
        string webhookPath,
        IOptions<TelegramOptions> options,
        ITelegramUpdateDispatcher dispatcher,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        var logger = loggerFactory.CreateLogger("MyBudget.Api.TelegramWebhook");
        var settings = options.Value;

        if (!settings.IsEnabled)
        {
            return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        var providedSecret = httpContext.Request.Headers[SecretHeaderName].ToString();

        if (!FixedTimeEquals(webhookPath, settings.WebhookPath)
            || !FixedTimeEquals(providedSecret, settings.WebhookSecret))
        {
            logger.LogWarning("TelegramWebhookRejected");
            return Results.NotFound();
        }

        using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync(cancellationToken);
        var update = TelegramJson.TryDeserializeUpdate(json);

        if (update is null)
        {
            // Telegram does not send malformed payloads; answering 200 avoids a retry storm
            // over something this bot could never process anyway.
            logger.LogWarning("TelegramWebhookUnparseable");
            return Results.Ok();
        }

        await dispatcher.DispatchAsync(update, cancellationToken);
        return Results.Ok();
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        if (expected.Length == 0)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(provided),
            Encoding.UTF8.GetBytes(expected));
    }
}
