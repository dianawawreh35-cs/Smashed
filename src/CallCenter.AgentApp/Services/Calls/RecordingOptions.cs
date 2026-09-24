using System.IO;

namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// Where call recordings are written on this laptop, from the
/// <c>Recording</c> section of appsettings.json (A-30, A-31).
/// </summary>
/// <remarks>
/// The laptop's copy is a staging post, not an archive. A finished recording is
/// uploaded to the server and kept here only until that upload is confirmed
/// (A-31), so this folder holds the last few calls rather than the last few
/// months. The server is where retention (A-33) applies.
/// </remarks>
public class RecordingOptions
{
    public const string SectionName = "Recording";

    /// <summary>
    /// Whether calls are recorded at all. On by default, because A-30 is a
    /// Must; the switch exists for a site that has not agreed to it yet, and
    /// for turning recording off while diagnosing something else.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Where the files go. Under the agent's own application data by default,
    /// beside the logs and the offline queue, so nothing is written anywhere a
    /// shared laptop's next user could stumble over it.
    /// </summary>
    public string Folder { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CallCenter",
        "recordings");
}
