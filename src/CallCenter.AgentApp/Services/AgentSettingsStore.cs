using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// The handful of choices that belong to the laptop rather than to the agent
/// (A-05): the UI language, light or dark (A-90), the phone's own switches, and
/// the username to pre-fill. There is no audio device choice: A-03 was removed on 27 Sep 2026,
/// and calls use the Windows defaults.
/// </summary>
/// <remarks>
/// Deliberately not a password store. Every agent has their own credentials, and
/// laptops are shared between shifts, so the password is always typed — only the
/// last username is kept, and only to save typing.
/// </remarks>
public class AgentSettingsStore(ILogger<AgentSettingsStore> logger)
{
    /// <summary>What is written to <c>settings.json</c>.</summary>
    public record Settings
    {
        /// <summary>Pre-filled in the login box. Never a password.</summary>
        public string? LastLogin { get; init; }

        /// <summary>"ar" or "en" (A-80).</summary>
        public string Language { get; init; } = "ar";

        /// <summary>Turn calls away without ringing (A-18).</summary>
        public bool DoNotDisturb { get; init; }

        /// <summary>Pick up an incoming call without pressing Answer (A-18).</summary>
        public bool AutoAnswer { get; init; }

        /// <summary>
        /// Do not disturb was turned on by Break in (A-86) and Break out has not
        /// turned it off yet. Set only while a break is going; found set at the
        /// next sign-in, it means the app stopped during a break, and that
        /// sign-in turns do not disturb off again.
        /// </summary>
        public bool BreakTurnedOnDoNotDisturb { get; init; }

        /// <summary>
        /// The side panel was folded down to a strip, to give the section
        /// beside it the room (Dia, 4 Oct 2026). Kept as it was left.
        /// </summary>
        public bool RailCollapsed { get; init; }

        /// <summary>"dark" or "light" (A-90). Dark until an agent chooses.</summary>
        public string Theme { get; init; } = ThemeService.Dark;

        /// <summary>
        /// A-88: each agent's Websites layout on this laptop, by the agent's
        /// id. Per agent, not per laptop, as their logins are.
        /// </summary>
        public Dictionary<string, WebsiteLayout> WebsiteLayouts { get; init; } = [];
    }

    /// <summary>
    /// How an agent left the Websites screen (A-88): one, two or four places,
    /// the tab in each, and each tab's zoom.
    /// </summary>
    /// <param name="Places">1, 2 or 4.</param>
    /// <param name="Tabs">The tab in each place, by website id; four entries, null for none.</param>
    /// <param name="Zoom">Each tab's zoom, by website id, 1.0 being 100 %.</param>
    /// <param name="Groups">The agent's own groups of tabs, opened together (A-88).</param>
    /// <param name="Copies">The agent's copies of a tab, by website id: the numbers, 2 to 4 ("POS 2").</param>
    public record WebsiteLayout(
        int Places, Guid?[] Tabs, Dictionary<string, double> Zoom, WebsiteGroup[]? Groups = null,
        Dictionary<string, int[]>? Copies = null);

    /// <summary>A group of up to four tabs an agent opens together (A-88), named by them.</summary>
    public record WebsiteGroup(string Name, Guid[] Tabs);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>The file. A property so the tests can keep it away from the real one.</summary>
    internal string FilePath { get; init; } = Path.Combine(App.AppDataDirectory, "settings.json");

    private Settings? _cached;

    public Settings Current => _cached ??= Load();

    /// <summary>Applies a change and writes it out.</summary>
    public void Update(Func<Settings, Settings> change)
    {
        _cached = change(Current);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_cached, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the username is an annoyance, not a
            // reason to stop the agent from working.
            logger.LogWarning(ex, "Could not save laptop settings to {Path}", FilePath);
        }
    }

    private Settings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings()
                : new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Laptop settings at {Path} could not be read; using defaults.", FilePath);
            return new Settings();
        }
    }
}
