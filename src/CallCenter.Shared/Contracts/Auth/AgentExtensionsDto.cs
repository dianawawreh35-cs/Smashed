namespace CallCenter.Shared.Contracts.Auth;

/// <summary>
/// The agent's two PBX extensions and their SIP secrets (SRS 2.3, A-01/A-02).
/// Both make and receive calls; they differ by who is on the other end.
/// </summary>
/// <remarks>
/// The secrets are decrypted for this response only. They are returned to the
/// Agent App at login and to nobody else — never to the supervisor UI, never in
/// a list endpoint, and never written to the app's log (N-05).
/// </remarks>
/// <param name="SipServer">Host or IP of the Yeastar S20 the app registers to.</param>
/// <param name="CustomerExtension">
/// The customer-facing extension: the one the queue rings, and the one used to
/// call a customer back.
/// </param>
/// <param name="InternalExtension">
/// The internal extension: other agents and the four branches.
/// </param>
public record AgentExtensionsDto(
    string SipServer,
    string CustomerExtension,
    string CustomerSecret,
    string InternalExtension,
    string InternalSecret);
