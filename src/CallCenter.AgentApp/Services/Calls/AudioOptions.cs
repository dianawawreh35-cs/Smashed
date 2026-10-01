namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// How the call audio is handled on this laptop, from the <c>Audio</c> section
/// of appsettings.json.
/// </summary>
public class AudioOptions
{
    public const string SectionName = "Audio";

    /// <summary>
    /// Whether the agent's microphone goes through the echo canceller (A-87).
    /// On by default; the switch exists for a laptop whose audio driver does not
    /// get on with it, and for comparing a call with and without it.
    /// </summary>
    public bool EchoCancellation { get; set; } = true;
}
