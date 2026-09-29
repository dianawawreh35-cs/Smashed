using System.Security.Claims;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.AgentApp;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.AgentAppInstaller;

/// <summary>
/// The Agent App's installer, served from the server (N-11, S-63): a supervisor
/// uploads it in the web app, and an agent signs in there and downloads it. The
/// same version as a zip can go beside it, for a laptop the installer fails on.
/// </summary>
/// <remarks>
/// Signed in, not anonymous, as Dia asked: the program carries the server's
/// address, and nobody outside the call centre has a reason to fetch it.
/// </remarks>
[ApiController]
[Route("api/agent-app")]
public class AgentAppInstallerController(AgentAppInstallerStore store) : ControllerBase
{
    /// <summary>Which version the laptops are offered. 404 <c>no_installer</c> before the first upload.</summary>
    [HttpGet]
    [Authorize(AuthPolicies.SignedIn)]
    [ProducesResponseType<AgentAppInstallerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AgentAppInstallerDto>> Current(CancellationToken ct)
    {
        var installer = await store.CurrentAsync(ct);
        return installer is null ? NoInstaller() : Ok(installer);
    }

    /// <summary>The installer itself, saved under its version's name.</summary>
    [HttpGet("installer")]
    [Authorize(AuthPolicies.SignedIn)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Download(CancellationToken ct)
    {
        var installer = await store.CurrentAsync(ct);
        var program = installer is null ? null : store.OpenProgram();

        if (program is null)
        {
            return NoInstaller();
        }

        // Never cached: the same address serves a different program after
        // the next upload.
        Response.Headers.CacheControl = "no-store";

        return File(program, "application/vnd.microsoft.portable-executable", installer!.FileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// Replaces the installer (S-63). The body is the program itself, not a
    /// form: 60–80 MB goes straight to disk rather than through the form
    /// reader's buffers.
    /// </summary>
    [HttpPut("installer")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [RequestSizeLimit(AgentAppInstallerStore.MaxBytes)]
    [ProducesResponseType<AgentAppInstallerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AgentAppInstallerDto>> Upload([FromQuery] string? version, CancellationToken ct)
    {
        var (installer, failure) = await store.SaveAsync(
            Request.Body,
            version?.Trim() ?? string.Empty,
            User.FindFirstValue(ClaimTypes.Name) ?? "unknown",
            ct);

        return failure is not null ? Problem(failure.Value) : Ok(installer);
    }

    /// <summary>The same version as a zip, for a laptop the installer will not run on.</summary>
    [HttpGet("zip")]
    [Authorize(AuthPolicies.SignedIn)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadZip(CancellationToken ct)
    {
        var installer = await store.CurrentAsync(ct);
        var zip = installer?.ZipFileName is null ? null : store.OpenZip();

        if (zip is null)
        {
            return NotFound(Code(StatusCodes.Status404NotFound, "No zip", "no_zip",
                "No zip has been uploaded with this version."));
        }

        Response.Headers.CacheControl = "no-store";

        return File(zip, "application/zip", installer!.ZipFileName, enableRangeProcessing: true);
    }

    /// <summary>
    /// Adds the zip of the version the installer already has (S-63). Refused
    /// for any other version, so the fallback is never an older app.
    /// </summary>
    [HttpPut("zip")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [RequestSizeLimit(AgentAppInstallerStore.MaxBytes)]
    [ProducesResponseType<AgentAppInstallerDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AgentAppInstallerDto>> UploadZip([FromQuery] string? version, CancellationToken ct)
    {
        var (installer, failure) = await store.SaveZipAsync(
            Request.Body,
            version?.Trim() ?? string.Empty,
            User.FindFirstValue(ClaimTypes.Name) ?? "unknown",
            ct);

        return failure is not null ? Problem(failure.Value) : Ok(installer);
    }

    private static ProblemDetails Code(int status, string title, string code, string detail)
    {
        var problem = new ProblemDetails { Title = title, Detail = detail, Status = status };
        problem.Extensions["code"] = code;
        return problem;
    }

    private ObjectResult NoInstaller()
    {
        var problem = new ProblemDetails
        {
            Title = "No installer",
            Detail = "No Agent App installer has been uploaded yet.",
            Status = StatusCodes.Status404NotFound,
        };
        problem.Extensions["code"] = "no_installer";

        return StatusCode(StatusCodes.Status404NotFound, problem);
    }

    private ObjectResult Problem(AgentAppInstallerStore.Failure failure)
    {
        var (status, code, detail) = failure switch
        {
            AgentAppInstallerStore.Failure.BadVersion =>
                (StatusCodes.Status400BadRequest, "bad_version", "The version must look like 0.4.1."),
            AgentAppInstallerStore.Failure.TooLarge =>
                (StatusCodes.Status400BadRequest, "too_large", "The file is larger than 400 MB."),
            AgentAppInstallerStore.Failure.NotAZip =>
                (StatusCodes.Status400BadRequest, "not_a_zip", "The file is not a zip."),
            AgentAppInstallerStore.Failure.NoInstaller =>
                (StatusCodes.Status409Conflict, "no_installer", "Upload the installer first; the zip goes with it."),
            AgentAppInstallerStore.Failure.VersionMismatch =>
                (StatusCodes.Status409Conflict, "version_mismatch", "The zip must be the installer's version."),
            _ => (StatusCodes.Status400BadRequest, "not_a_program", "The file is not a Windows program."),
        };

        var problem = new ProblemDetails { Title = "Installer not saved", Detail = detail, Status = status };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
