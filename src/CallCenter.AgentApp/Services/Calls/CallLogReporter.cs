using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.Extensions.Logging;

namespace CallCenter.AgentApp.Services.Calls;

/// <summary>
/// Reports every call to the server as it ends (A-14).
/// </summary>
/// <remarks>
/// Every call, not only the answered ones. A missed call, a call the agent
/// rejected, and a call rejected automatically because the number is blocked all
/// belong in the supervisor's reports — the blocked one because A-17 says so
/// explicitly, and the missed ones because "how many calls did we lose" is most
/// of what the reports are for.
///
/// Queue first, then send. A call is on disk before the request is attempted, so
/// the server being down, the VPN dropping or the laptop losing power costs
/// nothing. The queue is flushed on the next successful report and at sign-in.
/// </remarks>
public class CallLogReporter(
    ApiClient api,
    AgentSession session,
    CallLogQueue queue,
    ILogger<CallLogReporter> logger)
{
    /// <summary>
    /// Subscribes to a call service, so every call it finishes is reported.
    /// </summary>
    /// <remarks>
    /// Wired at startup rather than in the constructor: a reporter that
    /// subscribes to a service it was handed would make the two impossible to
    /// construct in either order, and the one that finished calls would depend
    /// on the one that files them.
    /// </remarks>
    public void Listen(CallService calls) =>
        calls.CallFinished += (_, call) =>
        {
            var extension = session.Extensions?.Extension;

            if (extension is null)
            {
                // No extension means no phone, so there should be no call to
                // report. Worth a line rather than silence if it ever happens.
                logger.LogWarning("A call finished with no extension configured; it cannot be reported");
                return;
            }

            // Fire and forget on purpose: this runs on a SIP thread as the call
            // tears down, and blocking it to wait for an HTTP round trip would
            // delay the next call. The queue is written synchronously inside,
            // before anything is awaited, so nothing is lost if the app closes.
            _ = ReportAsync(Describe(call, extension));
        };

    /// <summary>
    /// Records a finished call. Never throws: a reporting problem must not reach
    /// the agent, who has done nothing wrong and cannot act on it.
    /// </summary>
    public async Task ReportAsync(LogCallRequest call, CancellationToken ct = default)
    {
        queue.Enqueue(call);
        await FlushAsync(ct);
    }

    /// <summary>
    /// Sends everything waiting. Called after each call and at sign-in, so a
    /// laptop that was offline for a shift catches up as soon as somebody logs
    /// in on it.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (!session.IsSignedIn)
        {
            // Nothing can be sent without a token. The queue keeps until the
            // next sign-in, which is exactly what it is for.
            return;
        }

        var pending = queue.Pending();
        if (pending.Count == 0)
        {
            return;
        }

        var sent = new List<LogCallRequest>();

        foreach (var call in pending)
        {
            var result = await api.LogCallAsync(call, ct);

            if (result.IsOk)
            {
                sent.Add(call);
                continue;
            }

            // A refusal the server is certain about will never succeed, however
            // often it is retried, and a queue that retries it forever blocks
            // every call behind it. Drop it, loudly.
            if (result.ErrorCode is "unknown_value" or "invalid_request")
            {
                logger.LogError(
                    "The server refused a queued call ({Code}); it is discarded rather than retried forever",
                    result.ErrorCode);

                sent.Add(call);
                continue;
            }

            // Anything else - unreachable, a 500, an expired token - is worth
            // retrying. Stop here rather than working through the rest: they
            // will fail the same way, and the order is worth keeping.
            logger.LogInformation(
                "{Count} call(s) still waiting to reach the server ({Reason})",
                pending.Count - sent.Count, result.ErrorCode ?? "unreachable");

            break;
        }

        if (sent.Count > 0)
        {
            queue.Acknowledge(sent);
            logger.LogInformation("{Count} call(s) reported to the server", sent.Count);
        }
    }

    /// <summary>
    /// Turns a finished call into the report the server stores. The status is
    /// worked out here, from what actually happened.
    /// </summary>
    public static LogCallRequest Describe(FinishedCall call, string extension) => new(
        SipCallId: call.SipCallId,
        Extension: extension,
        Direction: Directions.In,
        Status: call.Outcome switch
        {
            CallOutcome.Answered => CommunicationStatuses.Answered,
            CallOutcome.RejectedByAgent => CommunicationStatuses.Rejected,
            CallOutcome.Blocked => CommunicationStatuses.Blocked,
            CallOutcome.Busy => CommunicationStatuses.Missed,
            _ => CommunicationStatuses.Missed,
        },
        RemoteNumber: call.Number,
        RemoteName: call.CallerName,
        StartedAt: call.StartedAt,
        AnsweredAt: call.AnsweredAt,
        EndedAt: call.EndedAt,
        Queue: call.Queue,
        LaptopId: LaptopInfo.LaptopId);
}
