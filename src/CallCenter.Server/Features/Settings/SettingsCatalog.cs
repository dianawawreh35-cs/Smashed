using CallCenter.Shared.Contracts.Settings;

namespace CallCenter.Server.Features.Settings;

/// <summary>
/// The settings the supervisor may change, and what counts as a valid value
/// (S-47).
/// </summary>
/// <remarks>
/// A closed list on purpose. The <c>settings</c> table is a key/value store, so
/// without one a typo would quietly create <c>pbx.hots</c> and the real setting
/// would stay blank with nothing to show for it. Anything not listed here is
/// refused.
/// </remarks>
public static class SettingsCatalog
{
    /// <summary>What a setting is, and how to check a new value.</summary>
    /// <param name="Validate">Null when the value is acceptable, otherwise why not.</param>
    public record Definition(
        string Key,
        string Kind,
        IReadOnlyList<string>? Options,
        Func<string, string?> Validate);

    /// <summary>How long an agent may edit their own classification (A-42).</summary>
    public static readonly IReadOnlyList<string> EditWindows = ["SameDay", "Always"];

    public static readonly IReadOnlyList<Definition> All =
    [
        // The PBX the Agent Apps register to (SRS 2.3). Blank is allowed: it is
        // how a fresh installation starts, and the app reports the phone as
        // unconfigured rather than failing.
        new("pbx.host", SettingKinds.Text, null, _ => null),

        new("callback.extension", SettingKinds.Text, null, _ => null),

        new("pbx.ami.enabled", SettingKinds.Boolean, null, ValidateBoolean),

        // S-48: the other agents' and the branches' extension numbers, comma
        // separated. A call whose other party is on this list is internal -
        // still recorded, but left out of the customer-facing reports. Blank is
        // valid: it means every call counts as a customer call.
        new("reports.internal_numbers", SettingKinds.Text, null, ValidateNumberList),

        // A-05: a shared laptop must not stay signed in after a shift.
        new("agent.idle_logout_minutes", SettingKinds.Integer, null, IntegerBetween(1, 480)),

        new("agent.edit_window", SettingKinds.Choice, EditWindows,
            value => EditWindows.Contains(value) ? null : $"must be one of {string.Join(", ", EditWindows)}"),

        // R-21: "answered within N seconds".
        new("sla.answer_seconds", SettingKinds.Integer, null, IntegerBetween(1, 600)),

        // A-33 / S-43: recordings are deleted after this many days.
        new("recording.retention_days", SettingKinds.Integer, null, IntegerBetween(1, 3650)),
    ];

    private static readonly Dictionary<string, Definition> ByKey =
        All.ToDictionary(d => d.Key, StringComparer.OrdinalIgnoreCase);

    public static Definition? Find(string key) =>
        ByKey.TryGetValue(key, out var definition) ? definition : null;

    /// <summary>
    /// A comma-separated list of extension numbers. Digits only - these are
    /// dialled numbers, and letting a name through would silently match nothing.
    /// </summary>
    private static string? ValidateNumberList(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var bad = value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(entry => !entry.All(char.IsDigit))
            .ToList();

        return bad.Count == 0
            ? null
            : $"must be numbers separated by commas; check {string.Join(", ", bad)}";
    }

    private static string? ValidateBoolean(string value) =>
        bool.TryParse(value, out _) ? null : "must be true or false";

    private static Func<string, string?> IntegerBetween(int min, int max) => value =>
        int.TryParse(value, out var parsed) && parsed >= min && parsed <= max
            ? null
            : $"must be a whole number between {min} and {max}";
}
