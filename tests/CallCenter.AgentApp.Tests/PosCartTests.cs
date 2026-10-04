using CallCenter.AgentApp.Services.Calls;
using CallCenter.AgentApp.Services.Websites;
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

    /// <summary>A-88: the POS tab inside the app takes the cart, with the number in its local form.</summary>
    [Fact]
    public void The_cart_goes_to_the_pos_tab_when_one_takes_it()
    {
        var tabs = new CartTab();
        var tab = new Target(accepts: true);
        tabs.Register(tab);

        Cart(tabs).Open("+970599123456");

        tab.Opened.Should().Equal("0599123456");
    }

    [Fact]
    public void No_tab_is_asked_for_a_number_the_pos_cannot_know()
    {
        var tabs = new CartTab();
        var tab = new Target(accepts: true);
        tabs.Register(tab);

        Cart(tabs).Open("201");

        tab.Opened.Should().BeEmpty();
    }

    [Fact]
    public void A_screen_that_has_gone_is_not_asked()
    {
        var tabs = new CartTab();
        var tab = new Target(accepts: true);
        tabs.Register(tab);
        tabs.Unregister(tab);

        tabs.Current.Should().BeNull("the signed-out screen's tab is gone; the browser takes the cart again");
    }

    private static PosCart Cart(CartTab tabs) =>
        new(Microsoft.Extensions.Options.Options.Create(new PosCartOptions { Enabled = true, UrlTemplate = "" }),
            tabs, Microsoft.Extensions.Logging.Abstractions.NullLogger<PosCart>.Instance);

    private sealed class Target(bool accepts) : CartTab.ITarget
    {
        public List<string> Opened { get; } = [];

        public bool TryOpen(string localNumber)
        {
            Opened.Add(localNumber);
            return accepts;
        }
    }
}
