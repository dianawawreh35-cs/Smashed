namespace CallCenter.Shared.Contracts.Pbx;

/// <summary>What an agent's phone is doing, as the PBX reports it (S-61).</summary>
public static class PhoneStates
{
    /// <summary>The server has not heard from the PBX about this extension.</summary>
    public const string Unknown = "Unknown";

    /// <summary>No phone is connected to the PBX on this extension.</summary>
    public const string Offline = "Offline";

    /// <summary>Connected, and not on a call.</summary>
    public const string Free = "Free";

    public const string Ringing = "Ringing";

    public const string InCall = "InCall";
}

/// <summary>One agent's phone, for the supervisor's screens (S-61).</summary>
/// <param name="State">One of <see cref="PhoneStates"/>.</param>
/// <param name="Since">When it went into that state, as far as the server has seen; null when unknown.</param>
public record AgentPhoneDto(
    Guid UserId,
    string DisplayName,
    string Extension,
    string State,
    DateTimeOffset? Since);

/// <summary>Every agent with an extension, and whether the watch on the PBX is working (S-61).</summary>
/// <param name="Live">The server is subscribed to the PBX and hearing back.</param>
/// <param name="Problem">
/// Why it is not live: <c>not_configured</c> when the server's own extension
/// is not set up, <c>no_answer</c> when the PBX is not answering.
/// </param>
public record AgentPhonesDto(bool Live, string? Problem, IReadOnlyList<AgentPhoneDto> Agents);
