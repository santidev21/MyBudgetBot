using FluentAssertions;
using MyBudget.Domain.Users;

namespace MyBudget.Domain.Tests.Users;

public sealed class UserTests
{
    [Fact]
    public void A_new_user_defaults_to_the_colombian_configuration()
    {
        var user = new User(123_456_789, "santidev", "Santiago");

        user.TelegramUserId.Should().Be(123_456_789);
        user.Username.Should().Be("santidev");
        user.DisplayName.Should().Be("Santiago");
        user.Currency.Should().Be("COP");
        user.TimeZone.Should().Be("America/Bogota");
        user.Language.Should().Be("es");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_non_positive_telegram_user_id_is_rejected(long telegramUserId)
    {
        FluentActions.Invoking(() => new User(telegramUserId))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_username_is_optional_because_Telegram_users_may_not_have_one()
    {
        var user = new User(999);

        user.Username.Should().BeNull();
        user.DisplayName.Should().BeNull();
    }

    [Fact]
    public void Profile_values_are_trimmed_and_bounded()
    {
        var user = new User(1, new string('u', 200), new string('d', 400));

        user.Username!.Length.Should().Be(User.MaxUsernameLength);
        user.DisplayName!.Length.Should().Be(User.MaxDisplayNameLength);
    }

    [Fact]
    public void Blank_profile_values_become_null()
    {
        var user = new User(1, "santidev", "Santiago");

        user.UpdateProfile("   ", "");

        user.Username.Should().BeNull();
        user.DisplayName.Should().BeNull();
    }

    [Fact]
    public void The_language_and_time_zone_can_be_changed()
    {
        var user = new User(1);

        user.ChangeLanguage("en");
        user.ChangeTimeZone("America/Mexico_City");

        user.Language.Should().Be("en");
        user.TimeZone.Should().Be("America/Mexico_City");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void A_blank_language_is_rejected(string? language)
    {
        FluentActions.Invoking(() => new User(1).ChangeLanguage(language!))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_blank_time_zone_is_rejected()
    {
        FluentActions.Invoking(() => new User(1).ChangeTimeZone("  "))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_currency_is_normalized_to_upper_case()
    {
        var user = new User(1);

        user.ChangeCurrency("cop");

        user.Currency.Should().Be("COP");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("CO")]
    [InlineData("COPP")]
    public void An_invalid_currency_code_is_rejected(string? currency)
    {
        FluentActions.Invoking(() => new User(1).ChangeCurrency(currency!))
            .Should().Throw<ArgumentException>();
    }
}
