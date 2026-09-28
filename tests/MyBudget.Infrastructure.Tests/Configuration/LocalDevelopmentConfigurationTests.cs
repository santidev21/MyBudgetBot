using FluentAssertions;
using Microsoft.Extensions.Configuration;
using MyBudget.Infrastructure.Configuration;

namespace MyBudget.Infrastructure.Tests.Configuration;

public sealed class LocalDevelopmentConfigurationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"mybudget-config-{Guid.NewGuid():N}");

    public LocalDevelopmentConfigurationTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Friendly_names_become_configuration_keys()
    {
        WriteDotEnv(
            "TELEGRAM_BOT_TOKEN=123:abc",
            "ALLOWED_TELEGRAM_USER_IDS=1,2",
            "TELEGRAM_USE_POLLING=true",
            "DEFAULT_TIME_ZONE=America/Bogota",
            "DATABASE_CONNECTION_STRING=Host=localhost;Port=5435");

        var resolved = LocalDevelopmentConfiguration.Resolve(new ConfigurationBuilder().Build(), _root);

        resolved["Telegram:BotToken"].Should().Be("123:abc");
        resolved["Telegram:AllowedUserIds"].Should().Be("1,2");
        resolved["Telegram:UsePolling"].Should().Be("true");
        resolved["Localization:DefaultTimeZone"].Should().Be("America/Bogota");
        resolved["ConnectionStrings:Database"].Should().Be("Host=localhost;Port=5435");
    }

    [Fact]
    public void An_explicit_environment_variable_wins_over_the_file()
    {
        WriteDotEnv("TELEGRAM_BOT_TOKEN=from-the-file");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:BotToken"] = "from-the-environment",
            })
            .Build();

        LocalDevelopmentConfiguration.Resolve(configuration, _root)
            .Should().NotContainKey("Telegram:BotToken");
    }

    [Fact]
    public void A_blank_placeholder_does_not_block_the_file()
    {
        // appsettings.json ships ConnectionStrings:Database as an empty string. Treating that
        // as a configured value would leave the connection string unset for every native run.
        WriteDotEnv("DATABASE_CONNECTION_STRING=Host=localhost;Port=5435");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = string.Empty,
            })
            .Build();

        LocalDevelopmentConfiguration.Resolve(configuration, _root)
            .Should().ContainKey("ConnectionStrings:Database")
            .WhoseValue.Should().Be("Host=localhost;Port=5435");
    }

    [Fact]
    public void Variables_nothing_reads_are_ignored_rather_than_invented()
    {
        WriteDotEnv("SOMETHING_ELSE=1", "TELEGRAM_BOT_TOKEN=123:abc");

        var resolved = LocalDevelopmentConfiguration.Resolve(new ConfigurationBuilder().Build(), _root);

        resolved.Should().ContainSingle().Which.Key.Should().Be("Telegram:BotToken");
    }

    [Fact]
    public void Every_variable_in_the_template_is_wired_or_explicitly_compose_only()
    {
        // A variable in .env.example that nothing reads is a trap: it looks configured and is
        // not. Either it maps to a configuration key, or it is consumed by Compose itself.
        var template = DotEnvFile.Parse(File.ReadAllLines(Path.Combine(RepositoryRoot(), ".env.example")));

        var unwired = template.Keys
            .Where(name => !LocalDevelopmentConfiguration.KeyMap.ContainsKey(name)
                           && !LocalDevelopmentConfiguration.ComposeOnlyVariables.Contains(name))
            .ToList();

        unwired.Should().BeEmpty();
    }

    [Fact]
    public void The_two_variable_sets_do_not_overlap()
    {
        LocalDevelopmentConfiguration.KeyMap.Keys
            .Should().NotIntersectWith(LocalDevelopmentConfiguration.ComposeOnlyVariables);
    }

    private void WriteDotEnv(params string[] lines) =>
        File.WriteAllLines(Path.Combine(_root, DotEnvFile.FileName), lines);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MyBudget.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("these tests run from inside the repository");
        return directory!.FullName;
    }
}
