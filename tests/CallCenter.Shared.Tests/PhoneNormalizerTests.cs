using CallCenter.Shared.Phone;
using FluentAssertions;
using Xunit;

namespace CallCenter.Shared.Tests;

public class PhoneNormalizerTests
{
    [Theory]
    // --- national mobile ---
    [InlineData("0599123456", "970599123456")]
    [InlineData("0567654321", "970567654321")]
    // --- separators are stripped ---
    [InlineData("059-912-3456", "970599123456")]
    [InlineData("059 912 3456", "970599123456")]
    [InlineData("(059) 912.3456", "970599123456")]
    // --- international prefixes ---
    [InlineData("+970599123456", "970599123456")]
    [InlineData("00970599123456", "970599123456")]
    [InlineData("970599123456", "970599123456")]
    [InlineData("+970 59 912 3456", "970599123456")]
    // --- a trunk zero after the country code is dropped ---
    [InlineData("9700599123456", "970599123456")]
    [InlineData("00970 0599123456", "970599123456")]
    // --- national landlines: 02 / 04 / 08 / 09 ---
    [InlineData("022345678", "97022345678")]
    [InlineData("042345678", "97042345678")]
    [InlineData("082345678", "97082345678")]
    [InlineData("092345678", "97092345678")]
    [InlineData("02-234-5678", "97022345678")]
    // --- subscriber-only forms get 970 prepended ---
    [InlineData("599123456", "970599123456")]
    [InlineData("22345678", "97022345678")]
    // --- Israeli numbers are recognised and kept as-is ---
    [InlineData("972501234567", "972501234567")]
    [InlineData("+972-50-123-4567", "972501234567")]
    [InlineData("00972501234567", "972501234567")]
    // --- extensions and service codes are never country-coded ---
    [InlineData("101", "101")]
    [InlineData("2001", "2001")]
    [InlineData("*100", "100")]
    // --- Arabic-Indic digits fold to ASCII ---
    [InlineData("٠٥٩٩١٢٣٤٥٦", "970599123456")]
    // --- unknown international numbers pass through as digits ---
    [InlineData("+44 20 7123 4567", "442071234567")]
    // --- empty / junk input ---
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("abc", "")]
    [InlineData(null, "")]
    public void Normalize_returns_expected_canonical_form(string? input, string expected)
    {
        PhoneNormalizer.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("0599123456", "599123456")]
    [InlineData("+970599123456", "599123456")]
    [InlineData("00970599123456", "599123456")]
    [InlineData("599123456", "599123456")]
    [InlineData("022345678", "022345678")]
    [InlineData("101", "101")]
    [InlineData("", "")]
    public void Last9_returns_the_trailing_nine_digits(string input, string expected)
    {
        PhoneNormalizer.Last9(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("0599123456", "+970599123456", "00970 599 123 456", "970599123456")]
    public void Last9_matches_across_every_spelling_of_the_same_number(
        string a, string b, string c, string d)
    {
        var keys = new[] { a, b, c, d }.Select(PhoneNormalizer.Last9).ToArray();
        keys.Should().AllBe(keys[0]);
    }

    [Theory]
    [InlineData("٠٥٩٩١٢٣٤٥٦", "0599123456")]
    [InlineData("+970 (59) 912-3456", "970599123456")]
    [InlineData("no digits here", "")]
    public void DigitsOnly_strips_everything_but_digits(string input, string expected)
    {
        PhoneNormalizer.DigitsOnly(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("0599123456", true)]
    [InlineData("022345678", true)]
    [InlineData("+970599123456", true)]
    [InlineData("972501234567", false)]
    [InlineData("+442071234567", false)]
    [InlineData("101", false)]
    [InlineData("", false)]
    public void IsPalestinian_detects_the_970_country_code(string input, bool expected)
    {
        PhoneNormalizer.IsPalestinian(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("101", true)]
    [InlineData("2001", true)]
    [InlineData("20015", true)]
    [InlineData("200156", false)]
    [InlineData("0599123456", false)]
    [InlineData("", false)]
    public void IsExtension_flags_short_internal_numbers(string input, bool expected)
    {
        PhoneNormalizer.IsExtension(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("0599123456", "+970 59 912 3456")]
    [InlineData("022345678", "+970 2 234 5678")]
    [InlineData("972501234567", "+972501234567")]
    [InlineData("101", "101")]
    [InlineData("", "")]
    public void Format_renders_a_readable_number(string input, string expected)
    {
        PhoneNormalizer.Format(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_is_idempotent()
    {
        var once = PhoneNormalizer.Normalize("059-912 3456");
        PhoneNormalizer.Normalize(once).Should().Be(once);
    }
}
