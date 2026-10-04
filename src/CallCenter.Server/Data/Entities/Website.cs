namespace CallCenter.Server.Data.Entities;

/// <summary>
/// A website the agents work in, shown as a tab inside the Agent App (A-88).
/// Table <c>websites</c>. Managed by the supervisor.
/// </summary>
public class Website
{
    public Guid Id { get; set; }

    public string NameAr { get; set; } = null!;

    public string NameEn { get; set; } = null!;

    /// <summary>Where the tab opens.</summary>
    public string Url { get; set; } = null!;

    /// <summary>One of <c>WebsiteLogins</c>.</summary>
    public string Login { get; set; } = null!;

    /// <summary>The shared login's username. Plain: it is not a secret.</summary>
    public string? Username { get; set; }

    /// <summary>
    /// The shared login's password, encrypted as the SIP secrets are (N-05), so
    /// a database dump does not hand it over.
    /// </summary>
    public string? PasswordSecret { get; set; }

    /// <summary>The site alerts with a sound: its tab is never put to sleep.</summary>
    public bool AlertsWithSound { get; set; }

    /// <summary>The caller's cart, with <c>{number}</c> (A-85). On the POS only.</summary>
    public string? CartUrl { get; set; }

    public string? UsernameSelector { get; set; }

    public string? PasswordSelector { get; set; }

    public string? SubmitSelector { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTimeOffset UpdatedAt { get; set; }
}
