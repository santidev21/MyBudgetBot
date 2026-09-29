using FluentAssertions;
using Microsoft.Extensions.Options;
using MyBudget.Application.Configuration;
using MyBudget.Application.Localization;
using MyBudget.Telegram.Conversations;
using MyBudget.Telegram.Options;
using MyBudget.Telegram.Presentation;

namespace MyBudget.Telegram.Tests;

public sealed class PresentationTests
{
    [Theory]
    [InlineData("hola", "hola")]
    [InlineData("<b>negrita</b>", "&lt;b&gt;negrita&lt;/b&gt;")]
    [InlineData("pan & leche", "pan &amp; leche")]
    [InlineData("3 < 5 > 2", "3 &lt; 5 &gt; 2")]
    public void User_text_cannot_change_the_structure_of_a_message(string input, string expected)
    {
        TelegramHtml.Escape(input).Should().Be(expected);
    }

    [Fact]
    public void A_description_with_markup_is_rendered_literally()
    {
        // The whole point: a user typing markup must see it as text, not as formatting.
        TelegramHtml.Escape("pagué <script>alert(1)</script>")
            .Should().Be("pagué &lt;script&gt;alert(1)&lt;/script&gt;");
    }

    [Theory]
    [InlineData("/start", "start")]
    [InlineData("/START", "start")]
    [InlineData("/Start@MyBudgetBot", "start")]
    [InlineData("/start extra arguments", "start")]
    [InlineData("  /help  ", null)]
    [InlineData("hola", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("/", "")]
    public void Commands_are_parsed_tolerantly(string? input, string? expected)
    {
        BotCommands.Parse(input).Should().Be(expected);
    }

    [Fact]
    public void Menu_labels_map_back_to_their_action_in_every_language()
    {
        var messages = new ResourceUserMessages(Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()));
        var menu = new MainMenu(messages);

        foreach (var language in messages.SupportedLanguages)
        {
            foreach (var key in MainMenu.ActionKeys)
            {
                var label = messages.Get(language, key);

                menu.MatchAction(language, label).Should().Be(key, $"'{label}' is the label for {key}");
            }
        }
    }

    [Fact]
    public void An_ordinary_message_is_not_a_menu_action()
    {
        var messages = new ResourceUserMessages(Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()));
        var menu = new MainMenu(messages);

        menu.MatchAction("es", "compré verduras").Should().BeNull();
    }

    [Fact]
    public void The_main_menu_is_a_persistent_reply_keyboard_with_seven_items()
    {
        var messages = new ResourceUserMessages(Microsoft.Extensions.Options.Options.Create(new LocalizationOptions()));
        var keyboard = new MainMenu(messages).ReplyKeyboard("es");

        keyboard.Kind.Should().Be(BotKeyboardKind.Reply);
        keyboard.Rows.Should().HaveCount(4);
        keyboard.Rows.SelectMany(row => row).Should().HaveCount(7);
        keyboard.Rows.SelectMany(row => row).Should().OnlyContain(button => button.CallbackData == null);
    }

    [Fact]
    public void A_telegram_less_configuration_is_valid()
    {
        // Without a token the service still runs for migrations and health checks.
        var validator = new TelegramOptionsValidator();

        validator.Validate(null, new TelegramOptions()).Succeeded.Should().BeTrue();
    }

    [Fact]
    public void A_token_without_a_secret_path_or_allowlist_is_rejected()
    {
        var validator = new TelegramOptionsValidator();

        var result = validator.Validate(null, new TelegramOptions { BotToken = "abc" });

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("WebhookSecret"));
        result.Failures.Should().Contain(failure => failure.Contains("WebhookPath"));
        result.Failures.Should().Contain(failure => failure.Contains("AllowedUserIds"));
        result.Failures.Should().Contain(failure => failure.Contains("PublicBaseUrl"));
    }

    [Fact]
    public void A_webhook_secret_without_a_token_is_rejected_as_half_configured()
    {
        var validator = new TelegramOptionsValidator();

        validator.Validate(null, new TelegramOptions { WebhookSecret = "a".PadRight(24, 'b') })
            .Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_short_secret_or_path_is_rejected()
    {
        var validator = new TelegramOptionsValidator();

        var result = validator.Validate(null, new TelegramOptions
        {
            BotToken = "abc",
            WebhookSecret = "short",
            WebhookPath = "short",
            AllowedUserIds = "1",
            PublicBaseUrl = "https://example.test",
        });

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public void A_complete_configuration_is_valid()
    {
        var validator = new TelegramOptionsValidator();

        var result = validator.Validate(null, new TelegramOptions
        {
            BotToken = "123:abc",
            WebhookSecret = "a".PadRight(24, 'b'),
            WebhookPath = "c".PadRight(24, 'd'),
            AllowedUserIds = "123456789",
            PublicBaseUrl = "https://mybudget.example.test",
        });

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void A_non_positive_user_rate_limit_is_rejected()
    {
        // The throttle is a security property; zero would silently turn it off.
        var validator = new TelegramOptionsValidator();

        var result = validator.Validate(null, new TelegramOptions
        {
            BotToken = "123:abc",
            WebhookSecret = "a".PadRight(24, 'b'),
            WebhookPath = "c".PadRight(24, 'd'),
            AllowedUserIds = "123456789",
            PublicBaseUrl = "https://mybudget.example.test",
            UserRateLimitPerMinute = 0,
        });

        result.Succeeded.Should().BeFalse();
        result.Failures.Should().Contain(failure => failure.Contains("UserRateLimitPerMinute"));
    }

    [Fact]
    public void Polling_does_not_require_a_public_base_url()
    {
        var validator = new TelegramOptionsValidator();

        var result = validator.Validate(null, new TelegramOptions
        {
            BotToken = "123:abc",
            WebhookSecret = "a".PadRight(24, 'b'),
            WebhookPath = "c".PadRight(24, 'd'),
            AllowedUserIds = "123456789",
            UsePolling = true,
        });

        result.Succeeded.Should().BeTrue(string.Join("; ", result.Failures ?? []));
    }

    [Fact]
    public void Access_fails_closed_when_the_allowlist_is_empty()
    {
        var options = new TelegramOptions { BotToken = "abc" };

        options.IsUserAllowed(123).Should().BeFalse();
    }

    [Fact]
    public void The_webhook_path_is_built_from_the_same_constant_that_is_mapped()
    {
        TelegramWebhook.BuildPath("secret-segment")
            .Should().Be($"{TelegramWebhook.RoutePrefix}/secret-segment");
    }
}
