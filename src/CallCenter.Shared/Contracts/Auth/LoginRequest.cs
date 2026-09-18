using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Auth;

/// <summary>
/// Credentials issued by the supervisor (A-01). The agent never types SIP
/// details — the server returns them in <see cref="LoginResponse"/>.
/// </summary>
/// <param name="Login">Username, matched case-insensitively.</param>
/// <param name="Password">Plain password, checked against the BCrypt hash.</param>
/// <param name="LaptopId">
/// Machine name of the laptop. Laptops are shared between shifts (A-05), so the
/// session row records which machine this login happened on.
/// </param>
/// <param name="AppVersion">Agent App version, recorded for support.</param>
public record LoginRequest(
    [Required, MaxLength(64)] string Login,
    [Required, MaxLength(256)] string Password,
    [MaxLength(128)] string? LaptopId = null,
    [MaxLength(32)] string? AppVersion = null);

/// <summary>
/// Why a sign-in was refused, sent as the <c>code</c> member of the problem
/// response. Clients translate these (A-80) rather than showing the English
/// <c>detail</c> the API also carries.
/// </summary>
public static class LoginErrorCodes
{
    public const string InvalidCredentials = "invalid_credentials";

    public const string AccountDisabled = "account_disabled";

    /// <summary>Set by the client, not the server: the boxes were left empty.</summary>
    public const string EmptyFields = "empty_fields";

    /// <summary>Set by the client: the server did not answer (A-04).</summary>
    public const string ServerUnreachable = "server_unreachable";

    /// <summary>Set by the client: the server answered with something unusable.</summary>
    public const string ServerError = "server_error";
}
