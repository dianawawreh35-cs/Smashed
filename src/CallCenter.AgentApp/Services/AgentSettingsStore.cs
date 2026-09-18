using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services;

/// <summary>
/// The handful of choices that belong to the laptop rather than to the agent
/// (A-03, A-05): audio devices, the UI language, and the username to pre-fill.
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

        /// <summary>Audio device ids, remembered per laptop (A-03).</summary>
        public string? MicrophoneDeviceId { get; init; }

        public string? SpeakerDeviceId { get; init; }

        public string? RingDeviceId { get; init; }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path = Path.Combine(App.AppDataDirectory, "settings.json");

    private Settings? _cached;

    public Settings Current => _cached ??= Load();

    /// <summary>Applies a change and writes it out.</summary>
    public void Update(Func<Settings, Settings> change)
    {
        _cached = change(Current);

        try
        {
            Directory.CreateDirectory(App.AppDataDirectory);
            File.WriteAllText(_path, JsonSerializer.Serialize(_cached, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not being able to remember the username is an annoyance, not a
            // reason to stop the agent from working.
            logger.LogWarning(ex, "Could not save laptop settings to {Path}", _path);
        }
    }

    private Settings Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(_path)) ?? new Settings()
                : new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Laptop settings at {Path} could not be read; using defaults.", _path);
            return new Settings();
        }
    }
}
