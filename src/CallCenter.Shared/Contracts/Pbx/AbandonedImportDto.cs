namespace CallCenter.Shared.Contracts.Pbx;

/// <summary>
/// The abandoned-call import (S-55) as the settings screen shows it: where the
/// PBX is, who the server logs in as, how often it checks, and how the last
/// check went.
/// </summary>
/// <param name="PasswordSet">
/// Whether a password is stored. The password itself never leaves the server:
/// the screen can replace it, not read it.
/// </param>
/// <param name="Configured">Address, username and password are all set, so the import runs.</param>
/// <param name="LastError">Why the last check failed; null when it succeeded.</param>
/// <param name="LastAdded">Abandoned calls the last successful check added.</param>
/// <param name="SyncedThrough">
/// The PBX's local day the next scheduled check starts from (<c>yyyy-MM-dd</c>).
/// Every day before it has been downloaded after it ended.
/// </param>
public record AbandonedImportDto(
    string Url,
    string Username,
    bool PasswordSet,
    int IntervalMinutes,
    bool Configured,
    DateTimeOffset? LastCheckedAt,
    DateTimeOffset? LastSucceededAt,
    string? LastError,
    int? LastAdded,
    string? SyncedThrough);

/// <summary>Changes the import's settings (S-55).</summary>
/// <param name="Url">The PBX's web address, e.g. <c>https://10.8.0.1</c>. Blank turns the import off.</param>
/// <param name="Password">A new password, or blank to keep the stored one.</param>
public record UpdateAbandonedImportRequest(
    string? Url,
    string? Username,
    string? Password,
    int IntervalMinutes);

/// <summary>What one fetch from the PBX found (S-55).</summary>
/// <param name="From">The first local day downloaded, <c>yyyy-MM-dd</c>.</param>
/// <param name="To">The last local day downloaded, <c>yyyy-MM-dd</c>.</param>
/// <param name="Calls">Every call in the download, answered or not.</param>
/// <param name="Abandoned">Incoming calls the PBX marked Abandoned.</param>
/// <param name="Added">Of those, the ones not saved before.</param>
/// <param name="RingsLinked">Agent App rings newly recognised as part of an abandoned call.</param>
public record AbandonedFetchResultDto(
    bool Ok,
    string? Error,
    string From,
    string To,
    int Calls,
    int Abandoned,
    int Added,
    int RingsLinked,
    AbandonedImportDto Status);
