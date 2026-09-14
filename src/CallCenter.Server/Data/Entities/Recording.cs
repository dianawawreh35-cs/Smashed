namespace CallCenter.Server.Data.Entities;

/// <summary>
/// The audio file for one call. Table <c>recordings</c>.
/// </summary>
/// <remarks>
/// The retention job (A-33) deletes the file and sets <see cref="DeletedAt"/>
/// once it is older than <c>recording.retention_days</c>; the row and the
/// communication stay, so reports over older periods remain correct.
/// </remarks>
public class Recording
{
    public Guid Id { get; set; }

    public Guid CommunicationId { get; set; }
    public Communication Communication { get; set; } = null!;

    /// <summary>Relative to the recordings root: yyyy/MM/dd/&lt;comm-id&gt;.wav</summary>
    public string Path { get; set; } = null!;

    public long? SizeBytes { get; set; }

    public int? DurationSec { get; set; }

    public string Format { get; set; } = "wav";

    public DateTimeOffset UploadedAt { get; set; }

    /// <summary>Set by the retention job when the file is removed. The row is kept.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
