namespace CallCenter.Shared.Contracts.Settings;

/// <summary>One row of the settings screen (S-47).</summary>
/// <param name="Kind">
/// How the browser should render and validate it: one of <see cref="SettingKinds"/>.
/// The server validates again regardless — this only shapes the input.
/// </param>
/// <param name="Options">The allowed values when <paramref name="Kind"/> is Choice.</param>
public record SettingDto(
    string Key,
    string Value,
    string Kind,
    IReadOnlyList<string>? Options,
    DateTimeOffset UpdatedAt,
    string? UpdatedByDisplayName);

/// <summary>How a setting is entered.</summary>
public static class SettingKinds
{
    public const string Text = "Text";
    public const string Integer = "Integer";
    public const string Boolean = "Boolean";
    public const string Choice = "Choice";
}
