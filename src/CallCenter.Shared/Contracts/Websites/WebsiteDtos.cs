using System.ComponentModel.DataAnnotations;

namespace CallCenter.Shared.Contracts.Websites;

/// <summary>
/// How a website tab is signed in to (A-88).
/// </summary>
public static class WebsiteLogins
{
    /// <summary>
    /// Each agent signs in themselves, and the app remembers it for that agent
    /// on that laptop. The POS.
    /// </summary>
    public const string Own = "own";

    /// <summary>
    /// The app signs in with the username and password the supervisor set. The
    /// agent never sees the password.
    /// </summary>
    public const string Shared = "shared";

    public static readonly IReadOnlyList<string> All = [Own, Shared];
}

/// <summary>A website tab as the supervisor's Websites page shows it. Never carries the password.</summary>
/// <param name="HasPassword">A password is stored, so the page can say so without showing it.</param>
/// <param name="CartUrl">
/// The caller's cart, with <c>{number}</c> where the number goes (A-85). Set on
/// the POS: answering a call opens it in this tab.
/// </param>
public record WebsiteDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string Url,
    string Login,
    string? Username,
    bool HasPassword,
    bool AlertsWithSound,
    string? CartUrl,
    string? UsernameSelector,
    string? PasswordSelector,
    string? SubmitSelector,
    int SortOrder,
    bool IsActive);

/// <summary>Adds or changes a website tab (A-88).</summary>
/// <param name="Password">
/// Null keeps the stored one, so the page never has to send it back; an empty
/// string removes it.
/// </param>
/// <param name="UsernameSelector">
/// Where the site's username box is, as a CSS selector, for a login page the
/// app's own search cannot read. Usually empty.
/// </param>
public record UpsertWebsiteRequest(
    [Required, MaxLength(60)] string NameAr,
    [Required, MaxLength(60)] string NameEn,
    [Required, MaxLength(500)] string Url,
    [Required, MaxLength(10)] string Login,
    [MaxLength(200)] string? Username,
    [MaxLength(200)] string? Password,
    bool AlertsWithSound,
    [MaxLength(500)] string? CartUrl,
    [MaxLength(300)] string? UsernameSelector,
    [MaxLength(300)] string? PasswordSelector,
    [MaxLength(300)] string? SubmitSelector,
    int SortOrder,
    bool IsActive);

/// <summary>
/// A website tab as the Agent App gets it at sign-in (A-88): the tabs on show,
/// with the shared login's password, which the app types into the site and
/// never shows.
/// </summary>
public record AgentWebsiteDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string Url,
    string Login,
    string? Username,
    string? Password,
    bool AlertsWithSound,
    string? CartUrl,
    string? UsernameSelector,
    string? PasswordSelector,
    string? SubmitSelector);
