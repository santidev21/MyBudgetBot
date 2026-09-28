using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MyBudget.Api.Tests;

/// <summary>
/// Starts the real application.
/// <para>
/// This exists because some defects are invisible to unit tests by construction: a singleton
/// consuming a scoped service, an options validator that rejects the shipped defaults, or a
/// route that was never mapped. All three only fail when the host actually builds.
/// </para>
/// </summary>
public sealed class ApiSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiSmokeTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private HttpClient CreateClient() => _factory
        .WithWebHostBuilder(builder => builder.UseSetting(
            "ConnectionStrings:Database",
            "Host=localhost;Port=5432;Database=mybudget;Username=none;Password=none"))
        .CreateClient();

    [Fact]
    public async Task The_application_starts_and_reports_liveness()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_reports_unhealthy_when_the_database_is_unreachable()
    {
        // Liveness must stay independent of the database; readiness must not lie.
        using var client = CreateClient();

        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task The_root_route_answers_without_content()
    {
        using var client = CreateClient();

        var response = await client.GetAsync("/");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task The_webhook_rejects_a_request_without_the_secret()
    {
        // Telegram is not configured in this environment, so the endpoint answers 503. Either
        // way it must not be an open door.
        using var client = CreateClient();

        var response = await client.PostAsync(
            "/telegram/webhook/not-the-secret", new StringContent("{}"));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.ServiceUnavailable);
    }
}
