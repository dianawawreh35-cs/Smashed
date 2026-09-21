using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services.Localization;

/// <summary>
/// Every label in the app, in the language the agent chose (A-80).
/// </summary>
/// <remarks>
/// The same shape as the supervisor app's <c>src/i18n</c>: one JSON file per
/// language with nested keys, Arabic as the default, and the choice remembered.
/// Views bind through the indexer — <c>{Binding [login.heading], Source=...}</c>
/// — so changing language refreshes every label in place, with no restart.
/// </remarks>
public class Localizer : INotifyPropertyChanged
{
    /// <summary>The languages A-80 asks for.</summary>
    public static readonly IReadOnlyList<string> SupportedLanguages = ["ar", "en"];

    /// <summary>Arabic: the agents' language. English is the option, not the default.</summary>
    public const string DefaultLanguage = "ar";

    private readonly AgentSettingsStore _settings;
    private readonly ILogger<Localizer> _logger;

    /// <summary>Flattened "section.key" to text, per language.</summary>
    private readonly Dictionary<string, Dictionary<string, string>> _strings = new(StringComparer.OrdinalIgnoreCase);

    public Localizer(AgentSettingsStore settings, ILogger<Localizer> logger)
    {
        _settings = settings;
        _logger = logger;

        foreach (var language in SupportedLanguages)
        {
            _strings[language] = Load(language);
        }

        var saved = settings.Current.Language;
        Current = SupportedLanguages.Contains(saved) ? saved : DefaultLanguage;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after the language changes, for code that is not a binding.</summary>
    public event EventHandler? LanguageChanged;

    /// <summary>The language in use: "ar" or "en".</summary>
    public string Current { get; private set; }

    /// <summary>Arabic reads right to left; English does not.</summary>
    public FlowDirection FlowDirection =>
        Current == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    /// <summary>True when Arabic is in use, for the language toggle button.</summary>
    public bool IsArabic => Current == "ar";

    /// <summary>
    /// The text for a key in a named language, rather than the current one.
    /// </summary>
    /// <remarks>
    /// For the few places that need both languages at once — the classification
    /// form keeps an Arabic and an English label per field, because the agent
    /// can switch language mid-call and the fields must not have to be rebuilt.
    /// </remarks>
    public string? InLanguage(string key, string language) =>
        _strings.TryGetValue(language, out var strings) && strings.TryGetValue(key, out var text)
            ? text
            : null;

    /// <summary>
    /// The text for a key such as <c>login.heading</c>. An unknown key falls back
    /// to Arabic and then to the key itself, so a missing translation shows up as
    /// an obviously wrong label rather than an empty screen.
    /// </summary>
    public string this[string key]
    {
        get
        {
            if (_strings.TryGetValue(Current, out var current) && current.TryGetValue(key, out var text))
            {
                return text;
            }

            if (_strings.TryGetValue(DefaultLanguage, out var fallback) && fallback.TryGetValue(key, out var arabic))
            {
                _logger.LogWarning("No {Language} text for '{Key}'; falling back to Arabic.", Current, key);
                return arabic;
            }

            _logger.LogWarning("No text at all for '{Key}'.", key);
            return key;
        }
    }

    /// <summary>Switches language and remembers the choice for this laptop.</summary>
    public void SetLanguage(string language)
    {
        if (!SupportedLanguages.Contains(language) || language == Current)
        {
            return;
        }

        Current = language;
        _settings.Update(s => s with { Language = language });

        // The empty indexer name tells WPF that every binding on this object is
        // stale, which re-reads all of them at once.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Current)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsArabic)));

        LanguageChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogInformation("Interface language set to {Language}", language);
    }

    /// <summary>Flips between the two languages, for the toggle in the UI.</summary>
    public void Toggle() => SetLanguage(Current == "ar" ? "en" : "ar");

    private Dictionary<string, string> Load(string language)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "i18n", $"{language}.json");
        var flattened = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            Flatten(document.RootElement, prefix: null, flattened);
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // A missing or broken language file must not stop an agent working:
            // the indexer falls back, and at worst labels read as their keys.
            _logger.LogError(ex, "Could not read the {Language} labels from {Path}", language, path);
        }

        return flattened;
    }

    /// <summary>Turns nested JSON objects into "section.key" entries.</summary>
    private static void Flatten(JsonElement element, string? prefix, Dictionary<string, string> into)
    {
        foreach (var property in element.EnumerateObject())
        {
            var key = prefix is null ? property.Name : $"{prefix}.{property.Name}";

            switch (property.Value.ValueKind)
            {
                case JsonValueKind.Object:
                    Flatten(property.Value, key, into);
                    break;

                case JsonValueKind.String:
                    into[key] = property.Value.GetString() ?? string.Empty;
                    break;
            }
        }
    }
}

/// <summary>Holds the WPF indexer-binding name in one place.</summary>
internal static class Binding
{
    /// <summary>
    /// WPF's convention: a change notification for "Item[]" invalidates every
    /// indexer binding on the object.
    /// </summary>
    public const string IndexerName = "Item[]";
}
