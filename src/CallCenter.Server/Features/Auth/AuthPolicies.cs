namespace CallCenter.Server.Features.Auth;

/// <summary>Authorization policy names, so controllers do not repeat role strings.</summary>
public static class AuthPolicies
{
    /// <summary>Any signed-in account.</summary>
    public const string SignedIn = "SignedIn";

    /// <summary>Supervisors only: reports, user management, settings (section 4).</summary>
    public const string SupervisorOnly = "SupervisorOnly";

    /// <summary>Agents only: the endpoints the Agent App calls for its own work.</summary>
    public const string AgentOnly = "AgentOnly";
}

/// <summary>Claim types this API issues beyond the standard set.</summary>
public static class AppClaims
{
    /// <summary>The <c>agent_sessions</c> row this token was issued for (A-05).</summary>
    public const string SessionId = "sid";
}
