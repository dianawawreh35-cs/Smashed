namespace CallCenter.Shared.Contracts.AgentApp;

/// <summary>
/// The installer the laptops are offered now, and the zip beside it (S-63).
/// Read by the web app's Agent App page, and by the Agent App itself to see
/// whether it is behind (A-82).
/// </summary>
/// <param name="Version">As the supervisor gave it at upload, e.g. <c>0.4.1</c>.</param>
/// <param name="FileName">What the browser saves the installer as.</param>
/// <param name="UploadedBy">The supervisor's login.</param>
/// <param name="ZipFileName">
/// The same version as a zip, for when the installer will not run on a laptop
/// (S-63). Null when none was uploaded with this version.
/// </param>
public record AgentAppInstallerDto(
    string Version,
    string FileName,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string UploadedBy,
    string? ZipFileName = null,
    long? ZipSizeBytes = null);
