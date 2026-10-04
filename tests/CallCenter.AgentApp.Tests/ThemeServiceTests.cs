using System.IO;
using CallCenter.AgentApp.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>Light or dark, remembered on the laptop (A-90).</summary>
public sealed class ThemeServiceTests : IDisposable
{
    private readonly string _file = Path.Combine(Path.GetTempPath(), $"theme-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_file);

    private AgentSettingsStore Settings() =>
        new(NullLogger<AgentSettingsStore>.Instance) { FilePath = _file };

    private static ThemeService Theme(AgentSettingsStore settings) =>
        new(settings, NullLogger<ThemeService>.Instance);

    [Fact]
    public void A_laptop_that_never_chose_is_dark()
    {
        var theme = Theme(Settings());

        theme.Current.Should().Be(ThemeService.Dark);
        theme.IsLight.Should().BeFalse();
    }

    [Fact]
    public void The_choice_outlasts_a_restart()
    {
        var theme = Theme(Settings());
        var changes = new List<string?>();
        theme.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        theme.Toggle();

        theme.IsLight.Should().BeTrue();
        changes.Should().Contain(nameof(ThemeService.IsLight), "the button's icon follows it");

        Theme(Settings()).Current.Should().Be(ThemeService.Light, "the next start reads the same file");
    }

    [Fact]
    public void Anything_else_in_the_file_is_dark()
    {
        Settings().Update(s => s with { Theme = "purple" });

        Theme(Settings()).Current.Should().Be(ThemeService.Dark);
    }
}
