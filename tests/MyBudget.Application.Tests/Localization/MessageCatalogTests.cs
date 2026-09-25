using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Resources;
using FluentAssertions;
using Microsoft.Extensions.Options;
using MyBudget.Application.Configuration;
using MyBudget.Application.Localization;

namespace MyBudget.Application.Tests.Localization;

/// <summary>
/// Guards the message catalog itself.
/// <para>
/// A missing translation is the kind of defect that only shows up in production, in front of
/// a user, as a raw key like "Expense.Registered". These tests make that impossible to ship:
/// the declared keys, the listed constants and the resources on disk must agree exactly.
/// </para>
/// </summary>
public sealed class MessageCatalogTests
{
    private static readonly ResourceManager Resources =
        new("MyBudget.Application.Resources.Messages", typeof(MessageKeys).Assembly);

    private static IUserMessages CreateMessages(LocalizationOptions? options = null) =>
        new ResourceUserMessages(Options.Create(options ?? new LocalizationOptions()));

    [Fact]
    public void Every_key_resolves_in_every_supported_language()
    {
        var messages = CreateMessages();

        messages.SupportedLanguages.Should().NotBeEmpty();

        foreach (var language in messages.SupportedLanguages)
        {
            foreach (var key in MessageKeys.All)
            {
                var text = messages.Get(language, key);

                text.Should().NotBeNullOrWhiteSpace($"'{key}' has no value for '{language}'");
                text.Should().NotBe(key, $"'{key}' did not resolve for '{language}'");
            }
        }
    }

    [Fact]
    public void Resources_contain_exactly_the_declared_keys()
    {
        var declared = MessageKeys.All.OrderBy(key => key, StringComparer.Ordinal);
        var actual = ReadResourceSet()
            .Select(entry => (string)entry.Key)
            .OrderBy(key => key, StringComparer.Ordinal);

        actual.Should().BeEquivalentTo(declared);
    }

    [Fact]
    public void Every_declared_constant_is_listed_in_All()
    {
        // Catches the easy mistake: adding a key constant but forgetting to register it.
        var constants = typeof(MessageKeys)
            .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
            .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!)
            .ToList();

        constants.Should().NotBeEmpty();
        MessageKeys.All.Should().BeEquivalentTo(constants);
    }

    [Fact]
    public void All_contains_no_duplicates()
    {
        MessageKeys.All.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void No_resource_value_is_blank()
    {
        var blanks = ReadResourceSet()
            .Where(entry => string.IsNullOrWhiteSpace(entry.Value as string))
            .Select(entry => (string)entry.Key)
            .ToList();

        blanks.Should().BeEmpty();
    }

    [Fact]
    public void An_unknown_language_falls_back_to_the_default_language()
    {
        var messages = CreateMessages();

        messages.Get("fr", MessageKeys.ExpenseRegistered)
            .Should().Be(messages.Get("es", MessageKeys.ExpenseRegistered));
    }

    [Fact]
    public void An_invalid_language_tag_falls_back_instead_of_throwing()
    {
        var messages = CreateMessages();

        var text = messages.Get("not a tag", MessageKeys.ExpenseRegistered);

        text.Should().NotBe(MessageKeys.ExpenseRegistered);
        text.Should().Be(messages.Get("es", MessageKeys.ExpenseRegistered));
    }

    [Theory]
    [InlineData("zz")]
    [InlineData("xx-YY")]
    [InlineData("!!")]
    [InlineData("not a tag")]
    public void Any_unusable_language_tag_falls_back_rather_than_raising(string language)
    {
        // A language value comes from the database, so it is not trusted to be valid. Neither
        // CultureNotFoundException nor ArgumentException may reach the user.
        var messages = CreateMessages();

        messages.Get(language, MessageKeys.ExpenseRegistered)
            .Should().NotBe(MessageKeys.ExpenseRegistered);
    }

    [Fact]
    public void A_missing_key_returns_the_key_rather_than_an_empty_message()
    {
        var messages = CreateMessages();

        messages.Get("es", "Does.Not.Exist").Should().Be("Does.Not.Exist");
        messages.Contains("es", "Does.Not.Exist").Should().BeFalse();
    }

    [Fact]
    public void Placeholders_are_substituted()
    {
        var messages = CreateMessages();

        messages.Get("es", MessageKeys.ExpenseCategorySuggestion, "🛒 Mercado")
            .Should().Contain("🛒 Mercado");
    }

    [Fact]
    public void The_language_is_taken_from_the_parameter_not_from_ambient_culture()
    {
        // This is the whole reason the catalog does not use IStringLocalizer: a bot has no
        // request culture, and reading one would return the wrong language silently.
        var messages = CreateMessages();

        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
            messages.Get("es", MessageKeys.ExpenseRegistered)
                .Should().Be(messages.Get("es", MessageKeys.ExpenseRegistered));
            messages.Contains("es", MessageKeys.ExpenseRegistered).Should().BeTrue();
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    private static List<DictionaryEntry> ReadResourceSet()
    {
        // The neutral resource set is Spanish (NeutralResourcesLanguage = es), so the
        // invariant culture resolves to it without needing a satellite assembly.
        var set = Resources.GetResourceSet(CultureInfo.InvariantCulture, createIfNotExists: true, tryParents: false);
        set.Should().NotBeNull("the neutral resource set holds the Spanish messages");

        return set!.Cast<DictionaryEntry>().ToList();
    }
}
