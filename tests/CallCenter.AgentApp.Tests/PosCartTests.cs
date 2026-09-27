using CallCenter.AgentApp.Services.Calls;
using FluentAssertions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The POS cart opened on answer (A-85) gets the caller's number in the one
/// form the POS knows, and is not opened for a number it cannot know.
/// </summary>
public class PosCartTests
{
    private const string Template = "https://smashed-ps.com/app/cart/{number}";

    [Theory]
    [InlineData("0599123456")]
    [InlineData("970599123456")]
    [InlineData("+970599123456")]
    [InlineData("00970 59-912-3456")]
    [InlineData("599123456")]
    public void A_mobile_goes_in_its_local_form_however_it_arrived(string number) =>
        PosCart.UrlFor(Template, number).Should().Be("https://smashed-ps.com/app/cart/0599123456");

    [Fact]
    public void A_landline_goes_in_its_local_form() =>
        PosCart.UrlFor(Template, "97022345678").Should().Be("https://smashed-ps.com/app/cart/022345678");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("anonymous")]
    [InlineData("201")]
    [InlineData("447700900123")]
    public void No_page_for_a_withheld_internal_or_foreign_number(string? number) =>
        PosCart.UrlFor(Template, number).Should().BeNull();
}
