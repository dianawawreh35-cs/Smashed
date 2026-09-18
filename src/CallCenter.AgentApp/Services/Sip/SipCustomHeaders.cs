using SIPSorcery.SIP;

namespace CallCenter.AgentApp.Services.Sip;

/// <summary>
/// The extra headers the PBX dialplan bolts onto an INVITE — at present, which
/// queue the call came through.
/// </summary>
/// <remarks>
/// The queue matters twice. The agent needs it on the pop-up before they speak,
/// because "Delivery" and "Complaints" are answered differently; and the
/// classification needs it afterwards, because a report of complaints per queue
/// is one of the things this system exists to produce.
///
/// SIPSorcery parses the headers it knows and drops everything else into
/// <see cref="SIPHeader.UnknownHeaders"/> as raw "Name: value" lines, so a
/// custom header has to be picked out by hand. Taken from the proof-of-concept
/// app, which reads the same header from this same PBX.
/// </remarks>
public static class SipCustomHeaders
{
    /// <summary>Set by the dialplan before Queue(), so the agent can see which queue rang.</summary>
    public const string QueueName = "X-Queue-Name";

    /// <summary>The queue this call came through, or null when it did not come via one.</summary>
    public static string? QueueFrom(SIPRequest invite) => Value(invite, QueueName);

    /// <summary>
    /// The first value of a custom header, or null when it is absent or empty.
    /// </summary>
    /// <remarks>
    /// Header names are case-insensitive (RFC 3261), and an Asterisk variable
    /// that was never set arrives as an empty header rather than no header at
    /// all — both mean "no queue".
    /// </remarks>
    public static string? Value(SIPRequest request, string name)
    {
        var unknown = request.Header?.UnknownHeaders;
        if (unknown is null)
        {
            return null;
        }

        foreach (var line in unknown)
        {
            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            if (!line.AsSpan(0, colon).Trim().Equals(name.AsSpan(), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var value = line[(colon + 1)..].Trim().Trim('"').Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }
}
