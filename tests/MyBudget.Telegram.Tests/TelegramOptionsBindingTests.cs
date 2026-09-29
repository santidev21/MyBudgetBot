using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MyBudget.Telegram.Options;

namespace MyBudget.Telegram.Tests;

/// <summary>
/// How configuration actually reaches <see cref="TelegramOptions"/>.
/// <para>
/// The allowlist arrives as a single environment variable, and whether a comma separated value
/// binds to an array decides how the property must be declared. An earlier <c>long[]</c>
/// property was bound from <c>"111,222"</c> and silently stayed empty, which — because access
/// fails closed — would have made the bot refuse its own owner. These tests pin the behaviour
/// that was measured, not the behaviour that was assumed.
/// </para>
/// </summary>
public sealed class TelegramOptionsBindingTests
{
    private static TelegramOptions Bind(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(
                setting => setting.Key, setting => (string?)setting.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<TelegramOptions>().Bind(configuration.GetSection(TelegramOptions.SectionName));

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<TelegramOptions>>().Value;
    }

    [Fact]
    public void One_configured_id_is_accepted()
    {
        var options = Bind(("Telegram:AllowedUserIds", "123456789"));

        options.AllowedUserIdsList.Should().Equal(123456789L);
        options.IsUserAllowed(123456789).Should().BeTrue();
        options.IsUserAllowed(1).Should().BeFalse();
    }

    [Fact]
    public void A_comma_separated_list_allows_several_people()
    {
        var options = Bind(("Telegram:AllowedUserIds", "111,222,333"));

        options.AllowedUserIdsList.Should().Equal(111L, 222L, 333L);
        options.IsUserAllowed(222).Should().BeTrue();
        options.IsUserAllowed(999).Should().BeFalse();
    }

    [Fact]
    public void Spaces_and_a_trailing_comma_are_tolerated()
    {
        var options = Bind(("Telegram:AllowedUserIds", " 111 , 222 ,"));

        options.AllowedUserIdsList.Should().Equal(111L, 222L);
    }

    [Fact]
    public void An_absent_or_empty_list_refuses_everyone()
    {
        Bind().IsUserAllowed(111).Should().BeFalse();
        Bind(("Telegram:AllowedUserIds", string.Empty)).IsUserAllowed(111).Should().BeFalse();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("111,abc")]
    [InlineData("-5")]
    [InlineData("0")]
    [InlineData("111 222")]
    [InlineData("1.5")]
    public void A_malformed_list_is_rejected_as_a_whole_rather_than_partially_applied(string value)
    {
        // Partially applying "111,abc" would let 111 in while hiding that 222 was never a
        // valid id. Startup validation refuses the whole configuration instead.
        TelegramOptions.TryParseAllowedUserIds(value, out var ids, out var error)
            .Should().BeFalse();

        ids.Should().BeEmpty();
        error.Should().NotBeNull();
    }

    [Fact]
    public void Scalars_bind_from_their_environment_variable_form()
    {
        var options = Bind(
            ("Telegram:UsePolling", "true"),
            ("Telegram:StaleUpdateMinutes", "30"),
            ("Telegram:UserRateLimitPerMinute", "12"),
            ("Telegram:BotToken", "123:abc"));

        options.UsePolling.Should().BeTrue();
        options.StaleUpdateMinutes.Should().Be(30);
        options.UserRateLimitPerMinute.Should().Be(12);
        options.IsEnabled.Should().BeTrue();
    }
}
