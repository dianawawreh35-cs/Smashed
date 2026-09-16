using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Settings;

/// <summary>
/// Changes one or more settings in one go (S-47).
/// </summary>
/// <remarks>
/// All or nothing: if any value is rejected, none are written. A screen that
/// saves six fields together should not leave three applied and three not.
/// </remarks>
public record UpdateSettingsRequest(
    [Required] IReadOnlyDictionary<string, string> Values);
