using CallCenter.Server.Data.Seed;
using CallCenter.Server.Features.Settings;
using CallCenter.Shared.Contracts.Settings;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The settings the supervisor may change, and what a valid value is (S-47).
/// </summary>
/// <remarks>
/// The catalogue is a closed list so a typo cannot quietly create a setting
/// nothing reads. These check the list matches what the seed creates, and that
/// each value is actually policed.
/// </remarks>
public class SettingsCatalogTests
{
    [Fact]
    public void Every_seeded_setting_can_be_edited()
    {
        // A setting that exists but has no way to change it is the gap S-47 was
        // written to close - it must not reappear silently.
        var seeded = SeedData.Settings.Select(s => s.Key);
        var editable = SettingsCatalog.All.Select(d => d.Key);

        editable.Should().BeEquivalentTo(seeded);
    }

    [Fact]
    public void An_unknown_key_is_not_in_the_catalogue()
    {
        SettingsCatalog.Find("pbx.hots").Should().BeNull();
    }

    [Fact]
    public void Keys_are_found_whatever_the_casing()
    {
        SettingsCatalog.Find("PBX.HOST")!.Key.Should().Be("pbx.host");
    }

    [Theory]
    [InlineData("recording.retention_days", "90", true)]
    [InlineData("recording.retention_days", "0", false)]
    [InlineData("recording.retention_days", "ninety", false)]
    [InlineData("recording.retention_days", "", false)]
    [InlineData("agent.idle_logout_minutes", "30", true)]
    [InlineData("agent.idle_logout_minutes", "-5", false)]
    [InlineData("agent.idle_logout_minutes", "100000", false)]
    [InlineData("sla.answer_seconds", "20", true)]
    [InlineData("sla.answer_seconds", "0", false)]
    [InlineData("agent.edit_window", "SameDay", true)]
    [InlineData("agent.edit_window", "Always", true)]
    [InlineData("agent.edit_window", "Whenever", false)]
    public void Values_are_policed(string key, string value, bool expectedValid)
    {
        var definition = SettingsCatalog.Find(key);
        definition.Should().NotBeNull();

        var problem = definition!.Validate(value);

        problem.Should().Match(p => expectedValid == (p == null));
    }

    [Fact]
    public void A_blank_pbx_host_is_allowed()
    {
        // How a fresh installation starts: the Agent App then reports the phone
        // as unconfigured rather than failing to sign in (A-01).
        SettingsCatalog.Find("pbx.host")!.Validate("").Should().BeNull();
    }

    [Fact]
    public void Choice_settings_publish_their_options()
    {
        // The browser renders a dropdown from these rather than hard-coding them.
        var editWindow = SettingsCatalog.Find("agent.edit_window")!;

        editWindow.Kind.Should().Be(SettingKinds.Choice);
        editWindow.Options.Should().BeEquivalentTo(["SameDay", "Always"]);
    }

    [Fact]
    public void Only_choice_settings_carry_options()
    {
        SettingsCatalog.All
            .Where(d => d.Kind != SettingKinds.Choice)
            .Should().OnlyContain(d => d.Options == null);
    }
}
