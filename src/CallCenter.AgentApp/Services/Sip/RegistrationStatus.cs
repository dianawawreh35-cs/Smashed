namespace CallCenter.AgentApp.Services.Sip;

/// <summary>
/// Where one extension's registration stands (A-02).
/// </summary>
/// <remarks>
/// The distinction between <see cref="Failed"/> and <see cref="Retrying"/> is
/// the point of this enum. A wrong password is not going to fix itself and the
/// agent needs their supervisor; a VPN that dropped will come back on its own
/// and the agent needs to wait. Showing both as "not working" would send them
/// to the wrong person.
/// </remarks>
public enum RegistrationStatus
{
    /// <summary>No attempt yet — nobody signed in, or no extensions configured.</summary>
    Idle,

    /// <summary>The first attempt is in flight.</summary>
    Registering,

    /// <summary>The PBX knows where this extension is. Calls can arrive.</summary>
    Registered,

    /// <summary>Refused outright — wrong credentials, or no such extension.</summary>
    Failed,

    /// <summary>Could not be reached, and another attempt is scheduled.</summary>
    Retrying,
}

/// <summary>Which of the agent's two extensions (SRS 2.3).</summary>
public enum ExtensionRole
{
    /// <summary>The one the queue rings, and that calls customers.</summary>
    Customer,

    /// <summary>Other agents and the four branches.</summary>
    Internal,
}

/// <summary>One extension's registration, as the UI reads it.</summary>
/// <param name="Detail">
/// The PBX's own words when it refused — a SIP status line such as
/// "403 Forbidden". Shown to the agent because "Failed" alone does not tell a
/// supervisor anything useful.
/// </param>
public record RegistrationState(
    ExtensionRole Role,
    string Extension,
    RegistrationStatus Status,
    string? Detail = null)
{
    public bool IsUsable => Status == RegistrationStatus.Registered;
}
