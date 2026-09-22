namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// How a number is dialled, from the <c>Dialing</c> section of appsettings.json
/// (A-20).
/// </summary>
/// <remarks>
/// This exists because <b>what the PBX wants dialled is a dialplan question, not
/// a code question</b>, and it cannot be answered from here. A Palestinian
/// mobile might be dialled as <c>0569498581</c>, or with the country code, or
/// behind a digit that reaches an outside line. Every wrong guess fails the same
/// way — the PBX answers 404 and no phone rings — so the format is configuration
/// rather than something compiled in and argued about.
///
/// The defaults send the number exactly as it is held, which is what a desk
/// phone beside the same switch does.
/// </remarks>
public class DialingOptions
{
    public const string SectionName = "Dialing";

    /// <summary>
    /// Put in front of every number dialled, for a switch that needs a digit to
    /// reach an outside line. Empty on Issabel unless its dialplan says
    /// otherwise.
    /// </summary>
    public string Prefix { get; set; } = string.Empty;

    /// <summary>
    /// How long to let a call ring before giving up, in seconds. Long enough for
    /// a customer to find their phone, short enough that an agent is not left
    /// holding a dead line.
    /// </summary>
    public int RingTimeoutSeconds { get; set; } = 45;
}
