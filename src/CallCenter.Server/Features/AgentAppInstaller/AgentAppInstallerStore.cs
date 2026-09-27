using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace CallCenter.Server.Features.AgentAppInstaller;

/// <summary>Where the Agent App's installer is kept on disk (N-11).</summary>
public class AgentAppInstallerOptions
{
    public const string SectionName = "AgentAppInstaller";

    /// <summary>
    /// The folder. Deployed as <c>/data/agent-app</c>, beside the recordings,
    /// so an update of the server image keeps it.
    /// </summary>
    [Required]
    public string Path { get; set; } = "data/agent-app";
}

/// <summary>The installer the laptops are offered now.</summary>
/// <param name="Version">As the supervisor gave it at upload, e.g. <c>0.4.1</c>.</param>
/// <param name="FileName">What the browser saves it as.</param>
/// <param name="UploadedBy">The supervisor's login.</param>
public record AgentAppInstallerDto(
    string Version,
    string FileName,
    long SizeBytes,
    DateTimeOffset UploadedAt,
    string UploadedBy);

/// <summary>
/// The one Agent App installer the server hands out (N-11, A-82): the latest a
/// supervisor uploaded. Agents sign in to the web app and download it.
/// </summary>
/// <remarks>
/// <para>
/// <b>One file, not a history.</b> An older version is never what a laptop
/// should install, and every version is rebuilt from its git tag if one is
/// needed again. So a new upload replaces the last, and the folder holds the
/// program and a small <c>current.json</c> saying what it is.
/// </para>
/// <para>
/// <b>Files rather than rows</b>, as with the menu photographs: 60–80 MB does
/// not belong in <c>pg_dump</c>. No migration either, which keeps a rollback
/// simple (RELEASING.md).
/// </para>
/// <para>
/// <b>Replaced, never half-written.</b> The upload goes to a temporary file and
/// is moved over the old one only once it is complete and checked, and the
/// description is written after it the same way. An agent downloading at that
/// moment keeps the file they started with: on Linux a rename leaves an open
/// file readable.
/// </para>
/// </remarks>
public partial class AgentAppInstallerStore(
    IOptions<AgentAppInstallerOptions> options,
    TimeProvider clock,
    ILogger<AgentAppInstallerStore> logger)
{
    /// <summary>
    /// The largest upload accepted. The self-contained app is about 185 MB
    /// unpacked and its installer about a third of that; this leaves room to
    /// grow without letting anyone fill the disk.
    /// </summary>
    public const long MaxBytes = 400L * 1024 * 1024;

    private const string ProgramFile = "SmashedAgentApp-Setup.exe";
    private const string DescriptionFile = "current.json";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root = options.Value.Path;

    public enum Failure
    {
        /// <summary>Not a version number: digits and dots, three or four parts.</summary>
        BadVersion,

        /// <summary>Empty, or not a Windows program at all.</summary>
        NotAProgram,

        /// <summary>Over <see cref="MaxBytes"/>.</summary>
        TooLarge,
    }

    /// <summary>
    /// A version the Agent App's own build could carry (publish.ps1 takes it
    /// from the git tag, v0.4.1 → 0.4.1, and 0.3.2.1 has happened).
    /// </summary>
    [GeneratedRegex(@"^\d{1,4}(\.\d{1,5}){2,3}$")]
    private static partial Regex VersionPattern();

    public static bool IsVersion(string? version) =>
        version is not null && VersionPattern().IsMatch(version);

    /// <summary>What is on offer, or null when nothing has been uploaded yet.</summary>
    public async Task<AgentAppInstallerDto?> CurrentAsync(CancellationToken ct = default)
    {
        var description = Path.Combine(_root, DescriptionFile);

        if (!File.Exists(description) || !File.Exists(Path.Combine(_root, ProgramFile)))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(description);
            return await JsonSerializer.DeserializeAsync<AgentAppInstallerDto>(stream, Json, ct);
        }
        catch (JsonException ex)
        {
            // Someone edited it by hand. Offer nothing rather than a guess; the
            // next upload writes it again.
            logger.LogWarning(ex, "{File} could not be read; no Agent App installer is offered", description);
            return null;
        }
    }

    /// <summary>The installer's bytes, for download. Null when there is none.</summary>
    public Stream? OpenProgram()
    {
        var path = Path.Combine(_root, ProgramFile);

        return File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true)
            : null;
    }

    /// <summary>
    /// Stores <paramref name="body"/> as the installer every laptop is now
    /// offered, replacing the last.
    /// </summary>
    public async Task<(AgentAppInstallerDto? Installer, Failure? Failure)> SaveAsync(
        Stream body, string version, string uploadedBy, CancellationToken ct = default)
    {
        if (!IsVersion(version))
        {
            return (null, Failure.BadVersion);
        }

        Directory.CreateDirectory(_root);

        var temporary = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.tmp");

        try
        {
            long size;
            var header = new byte[2];

            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await body.CopyToAsync(file, ct);
                size = file.Length;
            }

            if (size > MaxBytes)
            {
                return (null, Failure.TooLarge);
            }

            await using (var file = File.OpenRead(temporary))
            {
                var read = await file.ReadAsync(header, ct);

                // "MZ": how every Windows program begins. What the bytes are,
                // not what the file was called, so a zip or a document picked
                // by mistake is refused here rather than failing on a laptop.
                if (read < 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
                {
                    return (null, Failure.NotAProgram);
                }
            }

            File.Move(temporary, Path.Combine(_root, ProgramFile), overwrite: true);

            var installer = new AgentAppInstallerDto(
                version,
                $"SmashedAgentApp-Setup-{version}.exe",
                size,
                clock.GetUtcNow(),
                uploadedBy);

            var description = Path.Combine(_root, $"{DescriptionFile}.tmp");
            await File.WriteAllTextAsync(description, JsonSerializer.Serialize(installer, Json), ct);
            File.Move(description, Path.Combine(_root, DescriptionFile), overwrite: true);

            logger.LogInformation(
                "Agent App installer {Version} ({Size:N0} bytes) uploaded by {Login}",
                version, size, uploadedBy);

            return (installer, null);
        }
        finally
        {
            // Refused, cancelled or failed part way: nothing half-written stays.
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
