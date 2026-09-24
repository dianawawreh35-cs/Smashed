namespace CallCenter.Shared.Contracts.Communications;

/// <summary>
/// How much room the recordings take, and how much of it retention is keeping
/// (S-43).
/// </summary>
/// <remarks>
/// This is the number that tells the client whether 90 days is affordable. The
/// estimate when recording was built was about 50 GB for four agents, and
/// nobody had measured the real one; this is where the real one comes from.
///
/// <b>Two sizes, deliberately.</b> <paramref name="KeptBytes"/> is what the
/// database believes it is holding and <paramref name="DiskBytes"/> is what the
/// folder actually contains. They should be close. A large gap is worth seeing
/// rather than averaging away: files left behind by a failed retention run show
/// up as disk exceeding kept, and a folder that has been restored from a
/// backup half-way shows up the other way round.
/// </remarks>
/// <param name="RetentionDays">
/// The live value of <c>recording.retention_days</c> (S-43, S-47), so the
/// screen shows the number the nightly job will actually use.
/// </param>
/// <param name="Kept">Recordings whose audio is still on the server.</param>
/// <param name="KeptBytes">The sum of their recorded sizes.</param>
/// <param name="Expired">
/// Rows whose file retention has removed (A-33). They are counted, never
/// deleted: the call and its classification are kept indefinitely (N-08), and a
/// call must read as <i>recorded, expired</i> rather than as never recorded.
/// </param>
/// <param name="OldestKept">
/// When the oldest surviving recording was uploaded — in a healthy system, no
/// older than <paramref name="RetentionDays"/> ago.
/// </param>
/// <param name="NewestKept">When the newest one arrived. A stale value here means uploads have stopped.</param>
/// <param name="DiskBytes">What the recordings folder occupies, measured by walking it.</param>
/// <param name="DiskFiles">How many files are in it.</param>
/// <param name="EmptyFolders">
/// Date folders holding nothing. Nothing prunes them (see the decision entry of
/// 24 September); the count is here so the cost of that choice is visible
/// rather than assumed.
/// </param>
/// <param name="DiskReadable">
/// False when the folder could not be read at all — a missing mount, typically.
/// The database figures are still correct; the disk ones are zero and mean
/// nothing.
/// </param>
public record RecordingStorageDto(
    int RetentionDays,
    int Kept,
    long KeptBytes,
    int Expired,
    DateTimeOffset? OldestKept,
    DateTimeOffset? NewestKept,
    long DiskBytes,
    int DiskFiles,
    int EmptyFolders,
    bool DiskReadable);
