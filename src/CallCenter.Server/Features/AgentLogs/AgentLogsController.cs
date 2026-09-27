using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.AgentLogs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.AgentLogs;

/// <summary>
/// Where the Agent Apps send their logs (N-12), so a fault on a laptop can be
/// read without going to it.
/// </summary>
/// <remarks>
/// <b>Sending is for agents</b>, and only once signed in: a laptop that has not
/// signed in yet keeps its lines in its own file and sends them after. The
/// laptop is named by the app, not by the account, because laptops are shared
/// between shifts (A-05) and the file follows the machine.
///
/// <b>Reading is for supervisors</b>, on the web app's Logs page. The rule is on
/// each action, not the controller: an attribute on both would ask for both
/// roles at once, and nobody has both.
/// </remarks>
[ApiController]
[Route(AgentLogNames.Route)]
public class AgentLogsController(AgentLogStore store) : ControllerBase
{
    /// <summary>Every laptop that has sent a log, and its days with their errors and warnings.</summary>
    [HttpGet]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<IReadOnlyList<AgentLogLaptopDto>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<AgentLogLaptopDto>> Laptops() => Ok(store.Laptops());

    /// <summary>One day of one laptop's log, as entries, newest first.</summary>
    /// <param name="levels">One of <see cref="AgentLogLevels"/>; everything when left out.</param>
    /// <param name="search">Only entries that contain this, ignoring case.</param>
    [HttpGet("{laptop}/{file}")]
    [Authorize(AuthPolicies.SupervisorOnly)]
    [ProducesResponseType<AgentLogPageDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Entries(
        string laptop, string file, [FromQuery] string? levels, [FromQuery] string? search, CancellationToken ct)
    {
        var text = await store.ReadAsync(laptop, file, ct);

        return text is null
            ? Problem(StatusCodes.Status404NotFound, "log_not_found", "That laptop has no log for that day.")
            : Ok(AgentLogReader.Page(text, levels, search));
    }

    /// <summary>How much of each of the laptop's files the server already has.</summary>
    [HttpGet("{laptop}")]
    [Authorize(AuthPolicies.AgentOnly)]
    [ProducesResponseType<IReadOnlyDictionary<string, long>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyDictionary<string, long>> Lengths(string laptop) =>
        Ok(store.Lengths(laptop));

    /// <summary>
    /// Adds the next piece of one day's file. The body is the bytes, as they
    /// are in the laptop's file.
    /// </summary>
    /// <param name="offset">Where in the file the piece starts: how much the laptop believes the server has.</param>
    /// <response code="200">Added. The body says how long the file is now.</response>
    /// <response code="409">The server's copy does not end at <paramref name="offset"/>; nothing was added. The body says where it ends.</response>
    /// <response code="413">The file is at its size limit for the day; send no more of it.</response>
    [HttpPost("{laptop}/{file}")]
    [Authorize(AuthPolicies.AgentOnly)]
    [RequestSizeLimit(AgentLogNames.MaxChunkBytes + 4096)]
    [ProducesResponseType<AgentLogLengthDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<AgentLogLengthDto>(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> Append(string laptop, string file, [FromQuery] long offset, CancellationToken ct)
    {
        using var body = new MemoryStream();
        await Request.Body.CopyToAsync(body, ct);

        if (body.Length is 0 or > AgentLogNames.MaxChunkBytes)
        {
            return Problem(StatusCodes.Status400BadRequest, "bad_chunk",
                $"Send between 1 and {AgentLogNames.MaxChunkBytes} bytes at a time.");
        }

        var (outcome, length) = await store.AppendAsync(
            laptop, file, offset, body.GetBuffer().AsMemory(0, (int)body.Length), ct);

        return outcome switch
        {
            AgentLogStore.Outcome.Appended => Ok(new AgentLogLengthDto(length)),
            AgentLogStore.Outcome.WrongOffset => Conflict(new AgentLogLengthDto(length)),
            AgentLogStore.Outcome.TooLarge => Problem(StatusCodes.Status413PayloadTooLarge, "log_file_full",
                "This day's log is at its size limit on the server."),
            _ => Problem(StatusCodes.Status400BadRequest, "bad_name",
                "The laptop or file name is not one the server keeps logs under."),
        };
    }

    private ObjectResult Problem(int status, string code, string detail)
    {
        var problem = new ProblemDetails { Title = "Log not saved", Detail = detail, Status = status };
        problem.Extensions["code"] = code;

        return StatusCode(status, problem);
    }
}
