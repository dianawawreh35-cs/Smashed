using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Pbx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// The agents' phones as the PBX sees them (S-61), and listening in on a call
/// (S-62). Supervisors only.
/// </summary>
[ApiController]
[Route("api/pbx/agents")]
[Authorize(AuthPolicies.SupervisorOnly)]
public class PbxAgentsController(PbxListenService listen, ListenSessions sessions) : ControllerBase
{
    /// <summary>Header naming the listen-in, so the browser can stop it by id.</summary>
    public const string ListenIdHeader = "X-Listen-Id";

    /// <summary>What goes to the browser at once: 100 ms of sound, five of the PBX's packets.</summary>
    private const int PacketsPerWrite = 5;

    /// <summary>Every active agent with an extension: offline, free, ringing or in a call.</summary>
    [HttpGet]
    public async Task<ActionResult<AgentPhonesDto>> Phones(CancellationToken ct) =>
        Ok(await listen.PhonesAsync(ct));

    /// <summary>
    /// Listens in on the agent's call. Once the PBX has answered <c>*222</c>,
    /// the reply is the call's sound for as long as the listen-in lasts: raw
    /// 16-bit little-endian mono samples at 8 kHz (<c>audio/L16</c> apart from
    /// the byte order). It ends when the browser stops reading, on
    /// <see cref="Stop"/>, when the call ends, or after
    /// <see cref="PbxListenService.MaxListen"/>.
    /// </summary>
    [HttpGet("{agentId:guid}/listen")]
    public async Task<IActionResult> Listen(Guid agentId, CancellationToken ct)
    {
        var (started, failure, error) = await listen.StartAsync(agentId, User.GetRequiredUserId(), ct);
        if (failure is not null)
        {
            return Problem(failure.Value, error);
        }

        var why = "stopped";
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct, started!.Session.Stop.Token);
        limit.CancelAfter(PbxListenService.MaxListen);

        try
        {
            Response.ContentType = "application/octet-stream";
            Response.Headers.CacheControl = "no-store";
            Response.Headers[ListenIdHeader] = started.Session.Id.ToString();

            // Nothing between here and the browser may hold the sound back to
            // send it in one piece: not the server, and not a proxy in front.
            Response.Headers["X-Accel-Buffering"] = "no";
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
            await Response.StartAsync(limit.Token);

            var reader = started.Call.Audio;
            var buffer = new List<byte[]>(PacketsPerWrite);

            while (await reader.WaitToReadAsync(limit.Token))
            {
                while (buffer.Count < PacketsPerWrite && reader.TryRead(out var packet))
                {
                    buffer.Add(packet);
                }

                if (buffer.Count < PacketsPerWrite && !reader.Completion.IsCompleted)
                {
                    continue;
                }

                foreach (var packet in buffer)
                {
                    await Response.Body.WriteAsync(packet, limit.Token);
                }

                buffer.Clear();
                await Response.Body.FlushAsync(limit.Token);
            }

            why = "call_ended";
        }
        catch (OperationCanceledException)
        {
            why = ct.IsCancellationRequested ? "page_closed"
                : started.Session.Stop.IsCancellationRequested ? "stopped"
                : "time_limit";
        }
        finally
        {
            await listen.EndAsync(started, why);
        }

        return new EmptyResult();
    }

    /// <summary>Ends a listen-in this supervisor started. The browser also stops reading, which ends it too.</summary>
    [HttpDelete("listen/{id:guid}")]
    public IActionResult Stop(Guid id) =>
        sessions.Stop(id, User.GetRequiredUserId()) ? NoContent() : NotFound();

    /// <summary>A refusal, with a <c>code</c> for the browser's own translation (A-80).</summary>
    private ObjectResult Problem(PbxListenService.Failure failure, string? error)
    {
        var (status, code, detail) = failure switch
        {
            PbxListenService.Failure.NotFound =>
                (StatusCodes.Status404NotFound, "user_not_found", "No such agent."),
            PbxListenService.Failure.NoExtension =>
                (StatusCodes.Status409Conflict, "no_extension", "This agent has no extension."),
            PbxListenService.Failure.NotConfigured =>
                (StatusCodes.Status409Conflict, "not_configured", "The server's PBX extension is not set up."),
            _ => (StatusCodes.Status502BadGateway, "pbx_failed", error ?? "The PBX call did not work."),
        };

        var problem = new ProblemDetails { Title = "Listening refused", Detail = detail, Status = status };
        problem.Extensions["code"] = code;
        if (error is not null)
        {
            problem.Extensions["error"] = error;
        }

        return StatusCode(status, problem);
    }
}
