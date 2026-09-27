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

/// <summary>The installer the laptops are offered now, and the zip beside it.</summary>
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

/// <summary>
/// The one Agent App installer the server hands out (N-11, A-82): the latest a
/// supervisor uploaded, and optionally the same version as a zip. Agents sign
/// in to the web app and download either.
/// </summary>
/// <remarks>
/// <para>
/// <b>One version, not a history.</b> An older version is never what a laptop
/// should install, and every version is rebuilt from its git tag if one is
/// needed again. So a new upload replaces the last, and the folder holds the
/// program, perhaps the zip, and a small <c>current.json</c> saying what they are.
/// </para>
/// <para>
/// <b>The zip is always the installer's version.</b> It is accepted only for
/// the version the installer already has, and a new installer removes the old
/// zip. So the fallback an agent reaches for is never an older app than the
/// installer beside it.
/// </para>
/// <para>
/// <b>Files rather than rows</b>, as with the menu photographs: 60–80 MB does
/// not belong in <c>pg_dump</c>. No migration either, which keeps a rollback
/// simple (RELEASING.md).
/// </para>
/// <para>
/// <b>Replaced, never half-written.</b> An upload goes to a temporary file and
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
    /// unpacked, its installer about 70 MB and its zip about 80; this leaves
    /// room to grow without letting anyone fill the disk.
    /// </summary>
    public const long MaxBytes = 400L * 1024 * 1024;

    private const string ProgramFile = "SmashedAgentApp-Setup.exe";
    private const string ZipFile = "SmashedAgentApp.zip";
    private const string DescriptionFile = "current.json";

    /// <summary>How every Windows program begins.</summary>
    private static readonly byte[] ProgramHeader = "MZ"u8.ToArray();

    /// <summary>How every zip with anything in it begins.</summary>
    private static readonly byte[] ZipHeader = [(byte)'P', (byte)'K', 3, 4];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    private readonly string _root = options.Value.Path;

    /// <summary>One upload at a time, so an installer and its zip never cross.</summary>
    private readonly SemaphoreSlim _writing = new(1, 1);

    public enum Failure
    {
        /// <summary>Not a version number: digits and dots, three or four parts.</summary>
        BadVersion,

        /// <summary>Empty, or not a Windows program at all.</summary>
        NotAProgram,

        /// <summary>Empty, or not a zip.</summary>
        NotAZip,

        /// <summary>Over <see cref="MaxBytes"/>.</summary>
        TooLarge,

        /// <summary>A zip, but no installer has been uploaded for it to go with.</summary>
        NoInstaller,

        /// <summary>A zip for a version other than the installer's.</summary>
        VersionMismatch,
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

        AgentAppInstallerDto? current;
        try
        {
            await using var stream = File.OpenRead(description);
            current = await JsonSerializer.DeserializeAsync<AgentAppInstallerDto>(stream, Json, ct);
        }
        catch (JsonException ex)
        {
            // Someone edited it by hand. Offer nothing rather than a guess; the
            // next upload writes it again.
            logger.LogWarning(ex, "{File} could not be read; no Agent App installer is offered", description);
            return null;
        }

        // A zip named but gone from the folder is not offered.
        return current is { ZipFileName: not null } && !File.Exists(Path.Combine(_root, ZipFile))
            ? current with { ZipFileName = null, ZipSizeBytes = null }
            : current;
    }

    /// <summary>The installer's bytes, for download. Null when there is none.</summary>
    public Stream? OpenProgram() => Open(ProgramFile);

    /// <summary>The zip's bytes, for download. Null when there is none.</summary>
    public Stream? OpenZip() => Open(ZipFile);

    /// <summary>
    /// Stores <paramref name="body"/> as the installer every laptop is now
    /// offered, replacing the last, and removes the last version's zip.
    /// </summary>
    public async Task<(AgentAppInstallerDto? Installer, Failure? Failure)> SaveAsync(
        Stream body, string version, string uploadedBy, CancellationToken ct = default)
    {
        if (!IsVersion(version))
        {
            return (null, Failure.BadVersion);
        }

        await _writing.WaitAsync(ct);
        try
        {
            var (temporary, size, failure) = await ReceiveAsync(body, ProgramHeader, Failure.NotAProgram, ct);
            if (failure is not null)
            {
                return (null, failure);
            }

            File.Move(temporary!, Path.Combine(_root, ProgramFile), overwrite: true);

            // The zip was the old version's. Gone before the description says
            // the new one, so no moment offers an old zip beside a new installer.
            File.Delete(Path.Combine(_root, ZipFile));

            var installer = new AgentAppInstallerDto(
                version,
                $"SmashedAgentApp-Setup-{version}.exe",
                size,
                clock.GetUtcNow(),
                uploadedBy);

            await DescribeAsync(installer, ct);

            logger.LogInformation(
                "Agent App installer {Version} ({Size:N0} bytes) uploaded by {Login}",
                version, size, uploadedBy);

            return (installer, null);
        }
        finally
        {
            _writing.Release();
        }
    }

    /// <summary>
    /// Stores <paramref name="body"/> as the zip of the installer's version,
    /// for laptops where the installer will not run (S-63).
    /// </summary>
    public async Task<(AgentAppInstallerDto? Installer, Failure? Failure)> SaveZipAsync(
        Stream body, string version, string uploadedBy, CancellationToken ct = default)
    {
        if (!IsVersion(version))
        {
            return (null, Failure.BadVersion);
        }

        await _writing.WaitAsync(ct);
        try
        {
            var current = await CurrentAsync(ct);
            if (current is null)
            {
                return (null, Failure.NoInstaller);
            }

            if (current.Version != version)
            {
                return (null, Failure.VersionMismatch);
            }

            var (temporary, size, failure) = await ReceiveAsync(body, ZipHeader, Failure.NotAZip, ct);
            if (failure is not null)
            {
                return (null, failure);
            }

            File.Move(temporary!, Path.Combine(_root, ZipFile), overwrite: true);

            var installer = current with { ZipFileName = $"SmashedAgentApp-{version}.zip", ZipSizeBytes = size };
            await DescribeAsync(installer, ct);

            logger.LogInformation(
                "Agent App zip {Version} ({Size:N0} bytes) uploaded by {Login}",
                version, size, uploadedBy);

            return (installer, null);
        }
        finally
        {
            _writing.Release();
        }
    }

    private Stream? Open(string name)
    {
        var path = Path.Combine(_root, name);

        return File.Exists(path)
            ? new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true)
            : null;
    }

    /// <summary>
    /// Writes an upload to a temporary file and checks it. On success the
    /// caller moves the file into place; on any failure it is already gone.
    /// </summary>
    /// <param name="header">
    /// What the bytes must start with. What the file is, not what it was
    /// called, so the wrong one of the two files, picked by mistake, is refused
    /// here rather than failing on a laptop.
    /// </param>
    private async Task<(string? Temporary, long Size, Failure? Failure)> ReceiveAsync(
        Stream body, byte[] header, Failure wrongKind, CancellationToken ct)
    {
        Directory.CreateDirectory(_root);

        var temporary = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.tmp");
        var kept = false;

        try
        {
            long size;
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await body.CopyToAsync(file, ct);
                size = file.Length;
            }

            if (size > MaxBytes)
            {
                return (null, size, Failure.TooLarge);
            }

            var start = new byte[header.Length];
            int read;
            await using (var file = File.OpenRead(temporary))
            {
                read = await file.ReadAtLeastAsync(start, start.Length, throwOnEndOfStream: false, ct);
            }

            if (read < header.Length || !start.AsSpan().SequenceEqual(header))
            {
                return (null, size, wrongKind);
            }

            kept = true;
            return (temporary, size, null);
        }
        finally
        {
            // Refused, cancelled or failed part way: nothing half-written stays.
            if (!kept && File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }

    private async Task DescribeAsync(AgentAppInstallerDto installer, CancellationToken ct)
    {
        var description = Path.Combine(_root, $"{DescriptionFile}.tmp");
        await File.WriteAllTextAsync(description, JsonSerializer.Serialize(installer, Json), ct);
        File.Move(description, Path.Combine(_root, DescriptionFile), overwrite: true);
    }
}
