using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MyBudget.Application;
using MyBudget.Infrastructure;
using MyBudget.Infrastructure.Persistence;
using Serilog;
using Serilog.Formatting.Compact;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, loggerConfiguration) =>
{
    loggerConfiguration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "MyBudget.Api");

    // Structured JSON in production; readable text while developing.
    if (context.HostingEnvironment.IsDevelopment())
    {
        loggerConfiguration.WriteTo.Console();
    }
    else
    {
        loggerConfiguration.WriteTo.Console(new CompactJsonFormatter());
    }
});

var connectionString = builder.Configuration.GetConnectionString("Database");

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

builder.Services
    .AddHealthChecks()
    .AddNpgSql(connectionString!, name: "postgres", tags: new[] { "ready" });

var app = builder.Build();

// One-shot migration entrypoint used by the "migrator" container. The application
// never migrates on start: a failed migration must be loud, not a half-booted service.
if (args.Contains("--migrate", StringComparer.Ordinal))
{
    using var migrationScope = app.Services.CreateScope();
    var migrator = migrationScope.ServiceProvider.GetRequiredService<IDatabaseMigrator>();

    await migrator.MigrateAsync();

    Log.Information("Database migrations applied successfully.");
    return;
}

app.UseSerilogRequestLogging();

app.MapGet("/", () => Results.NoContent());

// Liveness: no dependencies, so a database outage never restarts the container.
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false,
});

// Readiness: checks PostgreSQL only.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready"),
});

// Full report, including timing. Restricted at the reverse proxy, never public.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = WriteHealthReportAsync,
});

await app.RunAsync();

static Task WriteHealthReportAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json; charset=utf-8";

    var payload = new
    {
        status = report.Status.ToString(),
        totalDurationMs = Math.Round(report.TotalDuration.TotalMilliseconds, 1),
        checks = report.Entries.Select(entry => new
        {
            name = entry.Key,
            status = entry.Value.Status.ToString(),
            durationMs = Math.Round(entry.Value.Duration.TotalMilliseconds, 1),
        }),
    };

    return context.Response.WriteAsJsonAsync(payload);
}

/// <summary>Exposed so integration tests can host the API with <c>WebApplicationFactory</c>.</summary>
public partial class Program;
