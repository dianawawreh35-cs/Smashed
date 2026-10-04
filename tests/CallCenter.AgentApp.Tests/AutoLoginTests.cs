using System.Text.Json;
using CallCenter.AgentApp.Services.Websites;
using CallCenter.Shared.Contracts.Websites;
using FluentAssertions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// The shared login a website tab types in by itself (A-88): only on the
/// site's own pages, never broken by what is in the password, and not tried
/// again and again once the site has refused it.
/// </summary>
public class AutoLoginTests
{
    private const string Site = "https://partners.example.com/login";

    private static AgentWebsiteDto Website(string password) => new(
        Guid.NewGuid(), "الطلبات", "Orders", Site, WebsiteLogins.Shared, "branch1@example.com", password,
        AlertsWithSound: true, CartUrl: null, null, null, null);

    [Theory]
    [InlineData("https://partners.example.com/login")]
    [InlineData("https://partners.example.com/anything?x=1")]
    [InlineData("https://example.com/")]
    [InlineData("https://login.example.com/sso")]
    public void The_login_goes_into_the_sites_own_pages(string page) =>
        AutoLogin.MayFillOn(Site, new Uri(page)).Should().BeTrue();

    [Theory]
    [InlineData("https://example.com.evil.net/login")]
    [InlineData("https://evilexample.com/login")]
    [InlineData("https://other.org/login")]
    [InlineData("file:///C:/login.html")]
    public void And_nowhere_else_the_tab_is_taken(string page) =>
        AutoLogin.MayFillOn(Site, new Uri(page)).Should().BeFalse();

    [Fact]
    public void No_page_no_login() => AutoLogin.MayFillOn(Site, null).Should().BeFalse();

    [Fact]
    public void A_password_with_quotes_backslashes_and_a_closing_tag_stays_one_value()
    {
        const string password = "p\"a\\ss'</script><b>";

        var script = AutoLogin.Script(Website(password));

        // The settings are the last argument: one JSON object, read back whole.
        var json = script[(script.LastIndexOf(")(", StringComparison.Ordinal) + 2)..^1];
        using var config = JsonDocument.Parse(json);

        config.RootElement.GetProperty("password").GetString().Should().Be(password);
        config.RootElement.GetProperty("username").GetString().Should().Be("branch1@example.com");
        config.RootElement.GetProperty("hosts").EnumerateArray().Select(h => h.GetString())
            .Should().Equal("partners.example.com", "example.com");
        script.Should().NotContain("</script>", "the encoder writes < as \\u003C");
    }

    [Fact]
    public void A_login_page_that_comes_back_within_a_minute_stops_the_tries_until_the_agent_asks()
    {
        var clock = new Clock();
        var gate = new AutoLogin.Gate(clock);

        gate.MayTry().Should().BeTrue();
        gate.Submitted();

        clock.Advance(TimeSpan.FromSeconds(20));
        gate.MayTry().Should().BeFalse("the site showed its login page again: the password was refused");
        gate.Failed.Should().BeTrue();

        clock.Advance(TimeSpan.FromHours(2));
        gate.MayTry().Should().BeFalse("a refused login is not tried again by itself");

        gate.Reset();
        gate.MayTry().Should().BeTrue("Reload asks again");
    }

    [Fact]
    public void A_session_that_ran_out_hours_later_signs_in_again()
    {
        var clock = new Clock();
        var gate = new AutoLogin.Gate(clock);

        gate.MayTry().Should().BeTrue();
        gate.Submitted();

        clock.Advance(TimeSpan.FromHours(3));
        gate.MayTry().Should().BeTrue();
        gate.Failed.Should().BeFalse();
    }

    [Fact]
    public void A_profile_name_is_the_agents_and_the_tabs_and_fits_webview2s_limit()
    {
        var agent = Guid.NewGuid();
        var site = Guid.NewGuid();

        var name = WebsiteEngine.ProfileName(agent, site);

        name.Length.Should().BeLessThanOrEqualTo(64);
        name.Should().NotBe(WebsiteEngine.ProfileName(Guid.NewGuid(), site), "another agent gets other logins");
        name.Should().NotBe(WebsiteEngine.ProfileName(agent, Guid.NewGuid()), "another tab gets other logins");
        name.Should().Be(WebsiteEngine.ProfileName(agent, site), "the same agent finds their logins again");
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 10, 4, 9, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
