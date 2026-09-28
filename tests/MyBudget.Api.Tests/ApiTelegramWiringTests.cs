using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyBudget.Api.Tests;

/// <summary>
/// Starts the real application with Telegram configured.
/// <para>
/// This exists because the default smoke test runs with Telegram disabled, which means the
/// polling service and the provisioner are never registered. Both are singletons that resolve
/// scoped services; getting that wrong only fails when the host is actually built. Polling is
/// off here, so no request is ever made to Telegram: a dummy token is enough to validate the
/// graph.
/// </para>
/// </summary>
public sealed class ApiTelegramWiringTests : IClassFixture<WebApplicationFactory<Program>>
{
    private const string DummyToken = "123456789:AAHtest-token-that-is-never-used";

    private readonly WebApplicationFactory<Program> _factory;

    public ApiTelegramWiringTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient CreateClient() => _factory
        .WithWebHostBuilder(builder =>
        {
            builder.UseSetting("LocalDevelopment:LoadDotEnv", "false");
            builder.UseSetting(
                "ConnectionStrings:Database",
                "Host=localhost;Port=5435;Database=mybudget;Username=none;Password=none");

            builder.UseSetting("Telegram:BotToken", DummyToken);
            builder.UseSetting("Telegram:WebhookSecret", new string('a', 32));
            builder.UseSetting("Telegram:WebhookPath", new string('b', 32));
            builder.UseSetting("Telegram:PublicBaseUrl", "https://example.test");
            builder.UseSetting("Telegram:AllowedUserIds", "1");
            builder.UseSetting("Telegram:UsePolling", "false");
        })
        .CreateClient();

    [Fact]
    public async Task The_host_starts_with_telegram_configured()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_webhook_is_mapped_and_gated_once_telegram_is_enabled()
    {
        using var client = CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/telegram/webhook/wrong-path")
        {
            Content = new StringContent("{}"),
        };
        request.Headers.Add("X-Telegram-Bot-Api-Secret-Token", "wrong-secret");

        var response = await client.SendAsync(request);

        // Configured, so it is no longer 503: a wrong path or secret is refused as not found.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
