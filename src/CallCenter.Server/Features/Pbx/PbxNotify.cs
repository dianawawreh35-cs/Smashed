using System.Xml;
using System.Xml.Linq;

namespace CallCenter.Server.Features.Pbx;

/// <summary>What a <c>dialog</c> NOTIFY says the extension is doing (S-61).</summary>
public enum DialogState
{
    Free,
    Ringing,
    InCall,
}

/// <summary>
/// Reads the bodies of the PBX's NOTIFY messages (S-61).
/// </summary>
/// <remarks>
/// Two event packages, because neither says everything. As Issabel (Asterisk
/// 11, chan_sip) sent them on 26 Sep:
/// <list type="bullet">
/// <item><b><c>dialog</c></b> (RFC 4235) says ringing (<c>early</c>) or in a call
/// (<c>confirmed</c>), but a phone that is not connected is <c>terminated</c>,
/// the same as a free one.</item>
/// <item><b><c>presence</c></b> (PIDF, RFC 3863) says whether a phone is
/// connected: <c>&lt;basic&gt;open&lt;/basic&gt;</c> with the note "Ready", or
/// <c>closed</c> with "Not online".</item>
/// </list>
/// </remarks>
public static class PbxNotify
{
    /// <summary>The dialog state, or null when the body is not dialog-info.</summary>
    public static DialogState? ReadDialog(string? body)
    {
        var doc = Parse(body);
        if (doc?.Root is null || doc.Root.Name.LocalName != "dialog-info")
        {
            return null;
        }

        var states = doc.Root.Elements()
            .Where(e => e.Name.LocalName == "dialog")
            .Select(d => d.Elements().FirstOrDefault(e => e.Name.LocalName == "state")?.Value.Trim())
            .ToList();

        // Several dialogs at once is a call with another ringing, or one on
        // hold behind another: talking wins over ringing.
        if (states.Contains("confirmed"))
        {
            return DialogState.InCall;
        }

        return states.Any(s => s is "early" or "proceeding" or "trying")
            ? DialogState.Ringing
            : DialogState.Free;
    }

    /// <summary>Whether a phone is connected, or null when the body is not PIDF.</summary>
    public static bool? ReadPresence(string? body)
    {
        var doc = Parse(body);
        if (doc?.Root is null || doc.Root.Name.LocalName != "presence")
        {
            return null;
        }

        var basic = doc.Descendants().Where(e => e.Name.LocalName == "basic").Select(e => e.Value.Trim()).ToList();
        return basic.Count == 0 ? null : basic.Contains("open");
    }

    private static XDocument? Parse(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(body.Trim());
        }
        catch (XmlException)
        {
            return null;
        }
    }
}
