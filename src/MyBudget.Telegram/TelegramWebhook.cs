namespace MyBudget.Telegram;

/// <summary>
/// The webhook route, defined here because it is Telegram knowledge rather than HTTP knowledge.
/// The API maps it; the provisioner registers it with Telegram. Both must agree.
/// </summary>
public static class TelegramWebhook
{
    public const string RoutePrefix = "/telegram/webhook";

    /// <summary>Random segment so the route is not guessable on its own; validated in constant time.</summary>
    public static string BuildPath(string webhookPath) => $"{RoutePrefix}/{webhookPath}";
}
