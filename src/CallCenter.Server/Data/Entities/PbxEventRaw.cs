using System.Text.Json;

namespace CallCenter.Server.Data.Entities;

/// <summary>
/// Raw PBX events, kept for 30 days so a reconciliation problem can be
/// investigated after the fact. Table <c>pbx_events_raw</c>.
/// </summary>
public class PbxEventRaw
{
    public long Id { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    /// <summary><see cref="Shared.PbxEventSources"/>: AMI, CDR or SIP.</summary>
    public string Source { get; set; } = null!;

    public string? EventName { get; set; }

    public JsonDocument Payload { get; set; } = null!;
}
