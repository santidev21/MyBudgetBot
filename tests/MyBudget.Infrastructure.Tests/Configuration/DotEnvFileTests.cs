using FluentAssertions;
using MyBudget.Infrastructure.Configuration;

namespace MyBudget.Infrastructure.Tests.Configuration;

public sealed class DotEnvFileTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"mybudget-dotenv-{Guid.NewGuid():N}");

    public DotEnvFileTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Parses_the_shapes_a_real_file_contains()
    {
        var values = DotEnvFile.Parse(
        [
            "# a comment",
            string.Empty,
            "   ",
            "PLAIN=value",
            "  SPACED_KEY  =  spaced value  ",
            "QUOTED=\"a value with spaces\"",
            "SINGLE='another one'",
            "WITH_EQUALS=Host=localhost;Port=5435",
            "EMPTY=",
            "no_equals_sign",
            "=no_key",
            "TRAILING_UNQUOTED=literal ",
        ]);

        values["PLAIN"].Should().Be("value");
        values["SPACED_KEY"].Should().Be("spaced value");
        values["QUOTED"].Should().Be("a value with spaces");
        values["SINGLE"].Should().Be("another one");
        values["WITH_EQUALS"].Should().Be("Host=localhost;Port=5435", "only the first = separates");
        values["EMPTY"].Should().BeEmpty();
        values["TRAILING_UNQUOTED"].Should().Be("literal");

        values.Should().NotContainKey("no_equals_sign");
        values.Should().NotContainKey(string.Empty);
        values.Should().NotContainKey("# a comment");
    }

    [Fact]
    public void A_quoted_value_keeps_an_inner_quote()
    {
        DotEnvFile.Parse(["KEY=\"it's fine\""])["KEY"].Should().Be("it's fine");
    }

    [Fact]
    public void Loads_the_file_found_by_walking_up()
    {
        File.WriteAllLines(Path.Combine(_root, DotEnvFile.FileName), ["TELEGRAM_BOT_TOKEN=from-the-file"]);
        var nested = Path.Combine(_root, "src", "MyBudget.Api");
        Directory.CreateDirectory(nested);

        // The nearest file wins, so a project directory two levels down still finds it.
        DotEnvFile.Find(nested).Should().Be(Path.Combine(_root, DotEnvFile.FileName));
        DotEnvFile.Load(nested)["TELEGRAM_BOT_TOKEN"].Should().Be("from-the-file");
    }

    [Fact]
    public void Missing_keys_yield_an_empty_result_rather_than_an_exception()
    {
        // The repository .env is gitignored, so a fresh clone has none.
        DotEnvFile.Load(Path.Combine(_root, "nowhere", "at", "all")).Should().NotBeNull();
    }

    [Fact]
    public void An_empty_file_parses_to_nothing()
    {
        DotEnvFile.Parse([]).Should().BeEmpty();
    }
}
