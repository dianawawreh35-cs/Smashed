using CallCenter.Shared.Text;
using FluentAssertions;
using Xunit;

namespace CallCenter.Shared.Tests;

/// <summary>
/// Name matching for the duplicate warning (A-63).
/// </summary>
/// <remarks>
/// PostgreSQL compares none of the Arabic spellings of a name as equal — the
/// database confirms <c>'أحمد' = 'احمد'</c> is false — so every fold here is the
/// difference between a warning firing and passing silently.
/// </remarks>
public class NameNormalizerTests
{
    [Theory]
    // The hamza is routinely left off when typing.
    [InlineData("أحمد", "احمد")]
    [InlineData("إبراهيم", "ابراهيم")]
    [InlineData("آمنة", "امنة")]
    // Diacritics are optional in writing.
    [InlineData("أَحْمَد", "احمد")]
    // Tatweel is decoration with no meaning.
    [InlineData("محـــمد", "محمد")]
    // Ta marbuta and ha are written either way at the end of a name.
    [InlineData("فاطمة", "فاطمه")]
    // Alef maqsura and ya likewise.
    [InlineData("يحيى", "يحيي")]
    // Hamza carriers.
    [InlineData("رؤوف", "رووف")]
    [InlineData("سائد", "سايد")]
    public void Arabic_spellings_of_one_name_match(string left, string right)
    {
        NameNormalizer.AreSame(left, right).Should().BeTrue();
    }

    [Theory]
    [InlineData("أحمد", "محمد")]
    [InlineData("سارة", "سميرة")]
    [InlineData("Ahmad", "Ahmad Ali")]
    public void Different_names_do_not_match(string left, string right)
    {
        NameNormalizer.AreSame(left, right).Should().BeFalse();
    }

    [Theory]
    [InlineData("Ahmad", "ahmad")]
    [InlineData("AHMAD", "Ahmad")]
    [InlineData("Ahmad  Ali", "Ahmad Ali")]
    [InlineData("  Ahmad Ali  ", "Ahmad Ali")]
    public void Latin_names_fold_case_and_spacing(string left, string right)
    {
        NameNormalizer.AreSame(left, right).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Nothing_normalises_to_nothing(string? name)
    {
        NameNormalizer.Normalize(name).Should().BeEmpty();
    }

    [Fact]
    public void Two_contacts_with_no_name_are_not_the_same_person()
    {
        // S-45 allows a bare number to be saved before anyone knows who it is.
        // Those must not all warn about each other.
        NameNormalizer.AreSame(null, null).Should().BeFalse();
        NameNormalizer.AreSame("", "").Should().BeFalse();
    }

    [Fact]
    public void The_normalised_form_is_for_matching_and_never_for_display()
    {
        // It is not how anyone spells their name - the stored name is shown.
        NameNormalizer.Normalize("أَحْمَد عَلي").Should().Be("احمد علي");
    }
}
