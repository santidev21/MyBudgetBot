using FluentAssertions;
using MyBudget.Application.Dates;

namespace MyBudget.Application.Tests.Dates;

public sealed class DateParserTests
{
    private static readonly DateOnly Today = new(2026, 9, 25);

    private static DateParseResult Parse(string? input) => TestServices.DateParser.Parse(input, Today);

    private static DateOnly DateOf(string input)
    {
        var result = Parse(input);
        result.Should().BeOfType<DateParseResult.Success>($"'{input}' should parse");
        return ((DateParseResult.Success)result).Date;
    }

    [Theory]
    [InlineData("hoy", 2026, 9, 25)]
    [InlineData("HOY", 2026, 9, 25)]
    [InlineData("hoy ", 2026, 9, 25)]
    [InlineData("ayer", 2026, 9, 24)]
    [InlineData("anteayer", 2026, 9, 23)]
    [InlineData("ante ayer", 2026, 9, 23)]
    [InlineData("antier", 2026, 9, 23)]
    public void Understands_the_relative_days_a_user_writes(string input, int year, int month, int day)
    {
        DateOf(input).Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("25/09", 2026, 9, 25)]
    [InlineData("24/09", 2026, 9, 24)]
    [InlineData("1/9", 2026, 9, 1)]
    [InlineData("25-09", 2026, 9, 25)]
    [InlineData("25.09", 2026, 9, 25)]
    public void Day_first_without_a_year_uses_the_most_recent_occurrence(
        string input, int year, int month, int day)
    {
        DateOf(input).Should().Be(new DateOnly(year, month, day));
    }

    [Fact]
    public void A_day_and_month_after_today_stays_in_the_past()
    {
        // On 2026-09-25, "25/12" is 2025-12-25: an expense is never in the future.
        DateOf("25/12").Should().Be(new DateOnly(2025, 12, 25));
    }

    [Theory]
    [InlineData("25/09/2026", 2026, 9, 25)]
    [InlineData("25-09-2026", 2026, 9, 25)]
    [InlineData("2026-09-25", 2026, 9, 25)]
    [InlineData("1/1/2020", 2020, 1, 1)]
    public void Understands_full_numeric_dates(string input, int year, int month, int day)
    {
        DateOf(input).Should().Be(new DateOnly(year, month, day));
    }

    [Theory]
    [InlineData("25 de septiembre de 2026", 2026, 9, 25)]
    [InlineData("25 de septiembre", 2026, 9, 25)]
    [InlineData("25 septiembre", 2026, 9, 25)]
    [InlineData("25 sep", 2026, 9, 25)]
    [InlineData("25 setiembre", 2026, 9, 25)]
    [InlineData("septiembre 25", 2026, 9, 25)]
    [InlineData("1 de enero de 2025", 2025, 1, 1)]
    [InlineData("31 de diciembre de 2025", 2025, 12, 31)]
    public void Understands_month_names_and_abbreviations(string input, int year, int month, int day)
    {
        DateOf(input).Should().Be(new DateOnly(year, month, day));
    }

    [Fact]
    public void Accents_in_month_names_do_not_matter()
    {
        // Users type "setiembre" and "septiembre"; a matcher that cares is wrong.
        DateOf("25 de setiembre").Should().Be(new DateOnly(2026, 9, 25));
    }

    [Theory]
    [InlineData(null, DateParseError.Empty)]
    [InlineData("", DateParseError.Empty)]
    [InlineData("   ", DateParseError.Empty)]
    [InlineData("abc", DateParseError.Unrecognized)]
    [InlineData("mañana", DateParseError.Unrecognized)]
    [InlineData("la semana pasada", DateParseError.Unrecognized)]
    [InlineData("31/02", DateParseError.InvalidDate)]
    [InlineData("25/13", DateParseError.InvalidDate)]
    [InlineData("32 de enero", DateParseError.InvalidDate)]
    [InlineData("25/09/26", DateParseError.InvalidDate)]
    [InlineData("26/09/2026", DateParseError.Future)]
    [InlineData("01/01/1999", DateParseError.TooOld)]
    public void Rejects_what_it_cannot_understand_or_cannot_represent(string? input, DateParseError expected)
    {
        var result = Parse(input);

        result.Should().BeOfType<DateParseResult.Invalid>();
        ((DateParseResult.Invalid)result).Reason.Should().Be(expected);
    }

    [Theory]
    [InlineData("25/09/2026/01", DateParseError.Unrecognized)]
    [InlineData("septiembre", DateParseError.InvalidDate)]
    [InlineData("25 septiembre 2026 extra", DateParseError.Unrecognized)]
    [InlineData("25 26 de septiembre", DateParseError.InvalidDate)]
    [InlineData("25 septiembre veinticinco", DateParseError.InvalidDate)]
    [InlineData("25 2026", DateParseError.Unrecognized)]
    [InlineData("0/01", DateParseError.InvalidDate)]
    [InlineData("ab/cd", DateParseError.Unrecognized)]
    [InlineData("septiembre octubre 2026", DateParseError.InvalidDate)]
    [InlineData("2026 septiembre 2025", DateParseError.InvalidDate)]
    public void Structurally_impossible_dates_are_reported_as_such(string? input, DateParseError expected)
    {
        var result = Parse(input);

        result.Should().BeOfType<DateParseResult.Invalid>();
        ((DateParseResult.Invalid)result).Reason.Should().Be(expected);
    }

    [Fact]
    public void Month_first_numeric_dates_are_refused_rather_than_guessed()
    {
        // 03/04 is 3 April in Colombia. Accepting 4 March as a fallback would silently swap
        // day and month, which is exactly the quiet misinterpretation this parser refuses.
        DateOf("03/04").Should().Be(new DateOnly(2026, 4, 3));
    }

    [Fact]
    public void Two_digit_years_are_refused()
    {
        Parse("25/09/26").Should().BeOfType<DateParseResult.Invalid>();
    }

    [Fact]
    public void Today_is_inclusive()
    {
        DateOf("hoy").Should().Be(Today);
        DateOf("25/09/2026").Should().Be(Today);
    }

    [Fact]
    public void The_input_is_preserved_in_the_failure_so_the_message_can_quote_it()
    {
        var result = (DateParseResult.Invalid)Parse("  la semana pasada  ");

        result.Input.Should().Be("  la semana pasada  ");
    }
}
