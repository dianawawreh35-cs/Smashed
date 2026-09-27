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
/// Agents only, and only once signed in: a laptop that has not signed in yet
/// keeps its lines in its own file and sends them after. The laptop is named
/// by the app, not by the account, because laptops are shared between shifts
/// (A-05) and the file follows the machine.
/// </remarks>
[ApiController]
[Route(AgentLogNames.Route)]
[Authorize(AuthPolicies.AgentOnly)]
public class AgentLogsController(AgentLogStore store) : ControllerBase
{
    /// <summary>How much of each of the laptop's files the server already has.</summary>
    [HttpGet("{laptop}")]
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
