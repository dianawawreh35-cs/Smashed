using System.ComponentModel.DataAnnotations;

namespace CallCenter.Server.Features.Auth;

/// <summary>
/// Bound from the <c>Jwt</c> configuration section.
/// </summary>
/// <remarks>
/// <see cref="SigningKey"/> is a deployment secret: it is set in the server's
/// environment (runbook step 5), never committed. Changing it signs every
/// current token out, which is the intended way to force a fleet-wide re-login.
/// </remarks>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    /// <summary>At least 32 bytes of key material, base64 or plain text.</summary>
    [Required, MinLength(32)]
    public string SigningKey { get; set; } = null!;

    public string Issuer { get; set; } = "callcenter";

    public string Audience { get; set; } = "callcenter";

    /// <summary>
    /// How long a token lasts. Long enough to cover a shift without a re-login,
    /// short enough that a disabled account stops working the same day.
    /// </summary>
    public TimeSpan Lifetime { get; set; } = TimeSpan.FromHours(12);
}
