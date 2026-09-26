namespace CallCenter.Server.Data.Entities;

/// <summary>
/// A number the server has put on the PBX's blacklist, or is trying to add or
/// remove (S-46). Table <c>pbx_blacklist</c>.
/// </summary>
/// <remarks>
/// The Blocked flag on the contact is what a supervisor decides; this is what
/// the PBX has been told. The two differ while a call to the PBX is pending or
/// has failed, and the sync closes the gap by dialling <c>*30</c> or
/// <c>*31</c>. Only numbers the server added are ever removed: anything put on
/// the PBX's blacklist by hand has no row here and is left alone.
/// </remarks>
public class PbxBlacklistEntry
{
    /// <summary>As keyed into the PBX: the local form, <c>0599123456</c>.</summary>
    public string Number { get; set; } = null!;

    /// <summary>The PBX has it: the last <c>*30</c> for it succeeded and no <c>*31</c> has since.</summary>
    public bool OnPbx { get; set; }

    /// <summary>When the <c>*30</c> succeeded.</summary>
    public DateTimeOffset? AddedAt { get; set; }

    public DateTimeOffset? LastAttemptAt { get; set; }

    /// <summary>Why the last call failed; null when it succeeded.</summary>
    public string? LastError { get; set; }

    /// <summary>Failed calls in a row. Reset by a success.</summary>
    public int FailedAttempts { get; set; }
}
