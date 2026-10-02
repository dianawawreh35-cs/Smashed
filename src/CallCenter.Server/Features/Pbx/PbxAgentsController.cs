using CallCenter.Server.Features.Auth;
using CallCenter.Shared.Contracts.Pbx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// The agents' phones as the PBX sees them (S-61), and listening in on a call,
/// or listening and speaking to the agent (S-62). Supervisors only.
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
    public Task<IActionResult> Listen(Guid agentId, CancellationToken ct) => StreamAsync(agentId, speak: false, ct);

    /// <summary>
    /// Listens in as <see cref="Listen"/> does, but through <c>*223</c>, so the
    /// supervisor can also speak to the agent; the customer does not hear them.
    /// The voice is posted to <see cref="Voice"/> while this reply lasts.
    /// </summary>
    [HttpGet("{agentId:guid}/speak")]
    public Task<IActionResult> Speak(Guid agentId, CancellationToken ct) => StreamAsync(agentId, speak: true, ct);

    /// <summary>The most one post of the supervisor's voice may carry: one second.</summary>
    public const int MaxVoiceBytes = VoiceBuffer.MaxSamples * 2;

    /// <summary>
    /// The supervisor's voice on their own <c>*223</c> listen-in: 16-bit
    /// little-endian mono samples at 8 kHz, about 100 ms a post, sent into the
    /// call as it comes.
    /// </summary>
    [HttpPost("listen/{id:guid}/voice")]
    public async Task<IActionResult> Voice(Guid id, CancellationToken ct)
    {
        var session = sessions.Find(id, User.GetRequiredUserId());
        if (session is null)
        {
            return NotFound();
        }

        // A *222 listen-in has no voice to send: the PBX would pass it to nobody.
        if (!session.Speak)
        {
            return Conflict();
        }

        // One byte past the limit is enough to know it is too much.
        var buffer = new byte[MaxVoiceBytes + 1];
        var length = 0;
        int read;
        while (length < buffer.Length && (read = await Request.Body.ReadAsync(buffer.AsMemory(length), ct)) > 0)
        {
            length += read;
        }

        if (length > MaxVoiceBytes)
        {
            return StatusCode(StatusCodes.Status413PayloadTooLarge);
        }

        session.Call.Speak(buffer.AsSpan(0, length));
        return NoContent();
    }

    private async Task<IActionResult> StreamAsync(Guid agentId, bool speak, CancellationToken ct)
    {
        var (started, failure, error) = await listen.StartAsync(agentId, User.GetRequiredUserId(), speak, ct);
        if (failure is not null)
        {
            return Problem(failure.Value, error);
        }

        var why = "stopped";
        using var callOver = new CancellationTokenSource();
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct, started!.Session.Stop.Token, callOver.Token);
        limit.CancelAfter(PbxListenService.MaxListen);

        // *222 stays on the line after the call it listens to has ended, so
        // the end of the call is taken from the PBX watch instead.
        _ = WatchForCallEndAsync(started.Extension, callOver, limit.Token);

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
                : callOver.IsCancellationRequested ? "call_ended"
                : "time_limit";
        }
        finally
        {
            // Also stops the check for the end of the call.
            await limit.CancelAsync();
            await listen.EndAsync(started, why);
        }

        return new EmptyResult();
    }

    /// <summary>How often the PBX watch is asked whether the call is over.</summary>
    public static readonly TimeSpan CallEndCheck = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Ends the listen-in once the call has been over for two checks running,
    /// so one stray report between a hold and a resume does not cut it off.
    /// </summary>
    private async Task WatchForCallEndAsync(string extension, CancellationTokenSource callOver, CancellationToken running)
    {
        var overFor = 0;
        try
        {
            while (!running.IsCancellationRequested)
            {
                await Task.Delay(CallEndCheck, running);
                overFor = listen.CallIsOver(extension) ? overFor + 1 : 0;
                if (overFor >= 2)
                {
                    await callOver.CancelAsync();
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ended some other way.
        }
        catch (ObjectDisposedException)
        {
            // The listen-in finished and its token went with it.
        }
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
