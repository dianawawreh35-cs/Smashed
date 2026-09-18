namespace CallCenter.Shared.Contracts.Auth;

/// <summary>
/// The agent's extension and its SIP secret (SRS 2.3, A-01/A-02).
/// </summary>
/// <remarks>
/// One extension, used for every call the agent handles. Internal calls are told
/// apart by the other party's number against the list in S-48, not by a second
/// extension.
///
/// This is the one DTO that carries a secret. It is returned by
/// <c>POST /api/auth/login</c> to an agent and nowhere else — never to the
/// supervisor UI, never in a list endpoint, and never written to the app's log
/// (N-05).
/// </remarks>
/// <param name="SipServer">Host or IP of the PBX the app registers to.</param>
public record AgentExtensionsDto(
    string SipServer,
    string Extension,
    string Secret);
