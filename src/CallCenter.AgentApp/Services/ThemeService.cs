using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// Light or dark (A-90): the agent's choice, remembered on the laptop as the
/// language is, and dark until somebody changes it.
/// </summary>
/// <remarks>
/// The colours are in two dictionaries with the same keys, Themes\Dark.xaml and
/// Themes\Light.xaml. App.xaml merges the dark one ahead of Theme.xaml; this
/// puts the chosen one in its place. Every use of a colour is a
/// DynamicResource, so the swap reaches the screens already open, the ringing
/// pop-up among them, with no restart.
/// </remarks>
public sealed class ThemeService : INotifyPropertyChanged
{
    public const string Dark = "dark";
    public const string Light = "light";

    private readonly AgentSettingsStore _settings;
    private readonly ILogger<ThemeService> _logger;

    public ThemeService(AgentSettingsStore settings, ILogger<ThemeService> logger)
    {
        _settings = settings;
        _logger = logger;

        // Anything but "light" is dark: a settings file from before A-90 has no
        // theme in it, and an agent who never chose keeps the look they know.
        Current = settings.Current.Theme == Light ? Light : Dark;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the palette changes, for code that is not a binding.</summary>
    public event EventHandler? ThemeChanged;

    /// <summary>"dark" or "light".</summary>
    public string Current { get; private set; }

    /// <summary>For the toggle: it shows the moon in light, the sun in dark.</summary>
    public bool IsLight => Current == Light;

    /// <summary>
    /// Puts the remembered palette in place. Once, at start-up, before the
    /// first window is built, so the app never opens in the other one.
    /// </summary>
    public void Apply() => Swap(Current);

    /// <summary>Flips between the two, for the button beside the language one.</summary>
    public void Toggle() => Set(IsLight ? Dark : Light);

    public void Set(string theme)
    {
        if (theme is not (Dark or Light) || theme == Current)
        {
            return;
        }

        Current = theme;
        _settings.Update(s => s with { Theme = theme });
        Swap(theme);

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLight)));
        ThemeChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Theme set to {Theme}", theme);
    }

    /// <summary>Where a theme's palette is, inside the program.</summary>
    internal static Uri PaletteUri(string theme) =>
        new($"/CallCenter.AgentApp;component/Themes/{(theme == Light ? "Light" : "Dark")}.xaml", UriKind.Relative);

    private static void Swap(string theme)
    {
        // No application in the unit tests that build view models.
        if (Application.Current is not { } app)
        {
            return;
        }

        var merged = app.Resources.MergedDictionaries;
        var palette = new ResourceDictionary { Source = PaletteUri(theme) };

        for (var i = 0; i < merged.Count; i++)
        {
            if (IsPalette(merged[i]))
            {
                merged[i] = palette;
                return;
            }
        }

        // First, so Theme.xaml after it can still override a key if it ever needs to.
        merged.Insert(0, palette);
    }

    private static bool IsPalette(ResourceDictionary dictionary) =>
        dictionary.Source?.OriginalString is { } source
        && (source.EndsWith("Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase)
            || source.EndsWith("Themes/Light.xaml", StringComparison.OrdinalIgnoreCase));
}
