using System.IO;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Classifications;
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
    /// Subscribes to a call service's finished recordings, so each one is
    /// uploaded and attached to its call (A-31).
    /// </summary>
    /// <remarks>
    /// Queued rather than sent, always. The recording is finished at the same
    /// moment the call is reported, so at that instant the server may not yet
    /// know the call exists; the shared queue is what puts it behind its call.
    /// </remarks>
    public void ListenForRecordings(CallService calls) =>
        calls.RecordingReady += (_, recording) =>
        {
            var extension = session.Extensions?.Extension;

            if (extension is null)
            {
                logger.LogWarning("A recording finished with no extension configured; it cannot be uploaded");
                return;
            }

            // Fire and forget, as with a finished call: this runs on a SIP
            // thread as the call tears down.
            _ = QueueRecordingAsync(
                new PendingRecording(recording.SipCallId, extension, recording.File.Path));
        };

    /// <summary>Queues a recording and tries to send it. Never throws.</summary>
    public async Task QueueRecordingAsync(PendingRecording recording, CancellationToken ct = default)
    {
        await queue.EnqueueAsync(recording, ct);
        await FlushAsync(ct);
    }

    /// <summary>
    /// Records a finished call. Never throws: a reporting problem must not reach
    /// the agent, who has done nothing wrong and cannot act on it.
    /// </summary>
    public async Task ReportAsync(LogCallRequest call, CancellationToken ct = default)
    {
        await queue.EnqueueAsync(call, ct);
        await FlushAsync(ct);
    }

    /// <summary>
    /// Records what a call was about (A-40).
    /// </summary>
    /// <remarks>
    /// Queued rather than sent, always, even with the server right there. The
    /// form opens when the call is answered and the call is not reported until
    /// it ends, so a classification saved while the agent is still talking would
    /// arrive for a call the server has never heard of. The shared queue puts it
    /// behind its call; the flush below sends it when its turn comes.
    ///
    /// Saving during the call is therefore normal and safe: the agent presses
    /// save, the form closes, and the ordering is somebody else's problem.
    /// </remarks>
    public async Task ClassifyAsync(
        SaveClassificationByCallRequest classification, CancellationToken ct = default)
    {
        await queue.EnqueueAsync(classification, ct);
        await FlushAsync(ct);
    }

    /// <summary>
    /// Records the note on a call that has just ended (A-41) — an outbound call
    /// the customer did not pick up.
    /// </summary>
    /// <remarks>
    /// Queued behind its call, like a classification, so it cannot arrive for a
    /// call the server has not heard of yet.
    /// </remarks>
    public async Task SaveNotesAsync(SaveCallNotesByCallRequest notes, CancellationToken ct = default)
    {
        await queue.EnqueueAsync(notes, ct);
        await FlushAsync(ct);
    }

    /// <summary>
    /// One pass over the queue at a time.
    /// </summary>
    /// <remarks>
    /// A call and its recording finish at the same instant, and each queued
    /// itself and flushed. Two passes then ran side by side: the first saw only
    /// the call and sent it; the second saw the call <i>and</i> the recording,
    /// sent the call again in the same millisecond, was refused as a duplicate,
    /// and stopped before the recording. The audio sat on the laptop until the
    /// next call (24 September).
    ///
    /// Now a second flush waits for the first and then makes its own pass, so
    /// it reads the queue afresh and finds what arrived meanwhile.
    /// </remarks>
    private readonly SemaphoreSlim _flushing = new(1, 1);

    /// <summary>
    /// Sends everything waiting. Called after each call, at sign-in and once a
    /// minute (<see cref="RetryEvery"/>), so a laptop that was offline for a
    /// shift catches up as soon as it can.
    /// </summary>
    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _flushing.WaitAsync(ct);

        try
        {
            await FlushPassAsync(ct);
        }
        finally
        {
            _flushing.Release();
        }
    }

    /// <summary>
    /// Retries whatever is waiting, on a timer, for as long as the app runs.
    /// </summary>
    /// <remarks>
    /// Without it a failed item waited for the next call or the next sign-in.
    /// A server that was restarted at lunch would leave the morning's last
    /// recording on the laptop until somebody's phone rang. A pass over an
    /// empty queue is one read of a small local file, so once a minute costs
    /// nothing.
    /// </remarks>
    public void RetryEvery(TimeSpan interval) =>
        _ = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(interval);

            while (await timer.WaitForNextTickAsync())
            {
                try
                {
                    await FlushAsync();
                }
                catch (Exception ex)
                {
                    // The next tick tries again; a timer that dies here would
                    // quietly end every retry for the rest of the shift.
                    logger.LogWarning(ex, "Retrying the queued call reports failed");
                }
            }
        });

    private async Task FlushPassAsync(CancellationToken ct)
    {
        if (!session.IsSignedIn)
        {
            // Nothing can be sent without a token. The queue keeps until the
            // next sign-in, which is exactly what it is for.
            return;
        }

        var pending = await queue.PendingItemsAsync(ct);
        if (pending.Count == 0)
        {
            return;
        }

        var done = new List<long>();

        // Set when a classification was skipped because its call had not been
        // sent yet. If a call did go out in this pass, one more pass clears it
        // rather than leaving it until the next call ends.
        var deferred = false;

        foreach (var item in pending)
        {
            var (id, call, classification, notes, recording) =
                (item.Id, item.Call, item.Classification, item.Notes, item.Recording);

            // One loop over every kind, in one sequence.
            bool ok;
            string? errorCode;

            if (call is not null)
            {
                var sent = await api.LogCallAsync(call, ct);
                (ok, errorCode) = (sent.IsOk, sent.ErrorCode);
            }
            else if (classification is not null)
            {
                var sent = await api.ClassifyByCallAsync(classification, ct);
                (ok, errorCode) = (sent.IsOk, sent.ErrorCode);
            }
            else if (notes is not null)
            {
                var sent = await api.SaveCallNotesByCallAsync(notes, ct);
                (ok, errorCode) = (sent.IsOk, sent.ErrorCode);
            }
            else
            {
                var sent = await api.UploadRecordingAsync(
                    recording!.SipCallId, recording.Extension, recording.LocalPath, ct);

                (ok, errorCode) = (sent.IsOk, sent.ErrorCode);

                // A-31: the laptop's copy goes only once the server has it. A
                // file whose upload succeeded but whose confirmation was lost
                // reports "recording_missing" on the retry, which is also done.
                if (ok || errorCode is "recording_missing")
                {
                    DeleteLocal(recording.LocalPath);
                    ok = true;
                }
            }

            if (ok)
            {
                done.Add(id);
                continue;
            }

            // A classification for a call the server does not have yet.
            //
            // This is the normal case, not the exception, and the queue order is
            // the opposite of what it looks like: the agent saves the form while
            // still talking, so the classification is queued *before* the call,
            // which is not reported until hang-up. Its id is therefore lower
            // than its call's.
            //
            // Skipped and left in place, never break. Stopping here would leave
            // the classification blocking the very call it is waiting for, and
            // the queue would deadlock - which is exactly what it did: two calls
            // and two classifications sat unsent behind each other.
            if (call is null && errorCode is "call_not_found")
            {
                logger.LogDebug(
                    "A classification, note or recording is waiting for its call to be reported ({Reference})",
                    classification?.SipCallId ?? notes?.SipCallId ?? recording?.SipCallId);

                deferred = true;
                continue;
            }

            // A refusal the server is certain about will never succeed, however
            // often it is retried, and a queue that retries it forever blocks
            // every call behind it. Drop it, loudly.
            if (errorCode is "unknown_value" or "invalid_request"
                or "unknown_type" or "unknown_branch" or "not_your_call"
                or "not_answered" or "notes_not_taken" or "edit_window_closed")
            {
                logger.LogError(
                    "The server refused queued item {Id} ({Code}); it is discarded rather than retried forever",
                    id, errorCode);

                done.Add(id);
                continue;
            }

            // Anything else - unreachable, a 500, an expired token - is worth
            // retrying. Stop here rather than working through the rest: they
            // will fail the same way, and the order is worth keeping, because a
            // classification must never reach the server before its call.
            await queue.RecordFailureAsync(id, errorCode, ct);

            logger.LogInformation(
                "{Count} item(s) still waiting to reach the server ({Reason})",
                pending.Count - done.Count, errorCode ?? "unreachable");

            break;
        }

        if (done.Count > 0)
        {
            await queue.AcknowledgeAsync(done, ct);
            logger.LogInformation("{Count} item(s) reported to the server", done.Count);
        }

        // A classification was skipped, and a call went out in the same pass -
        // very likely the call it was waiting for. One more pass sends it now
        // rather than leaving it queued until the next call ends.
        //
        // Guarded by done.Count so this can never loop: a pass that sent
        // nothing cannot have changed the answer.
        if (deferred && done.Count > 0 && !_retrying)
        {
            try
            {
                // The pass itself, not FlushAsync: this pass already holds the
                // lock, and asking for it again would wait on itself forever.
                _retrying = true;
                await FlushPassAsync(ct);
            }
            finally
            {
                _retrying = false;
            }
        }
    }

    /// <summary>
    /// Guards the single extra pass above against a chain of retries. One level
    /// is all that is ever needed: the second pass has its calls already sent.
    /// </summary>
    private bool _retrying;

    /// <summary>
    /// Removes the laptop's copy of a recording the server has taken (A-31).
    /// </summary>
    /// <remarks>
    /// Failing to delete is worth a line and nothing more: the recording is
    /// safely on the server, and the worst case is a file left on a laptop
    /// that the next upload will overwrite anyway.
    /// </remarks>
    private void DeleteLocal(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
                logger.LogInformation("The uploaded recording has been removed from this laptop");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "An uploaded recording could not be removed from this laptop");
        }
    }

    /// <summary>
    /// Turns a finished call into the report the server stores. The status is
    /// worked out here, from what actually happened.
    /// </summary>
    public static LogCallRequest Describe(FinishedCall call, string extension) => new(
        SipCallId: call.SipCallId,
        Extension: extension,
        // A-21: an outbound call is logged exactly like an inbound one, and the
        // only thing that differs is which way it went.
        Direction: call.IsOutbound ? Directions.Out : Directions.In,
        Status: call.Outcome switch
        {
            CallOutcome.Answered => CommunicationStatuses.Answered,
            CallOutcome.RejectedByAgent => CommunicationStatuses.Rejected,
            CallOutcome.Blocked => CommunicationStatuses.Blocked,
            CallOutcome.Busy => CommunicationStatuses.Missed,

            // NoAnswer, not Missed: Missed is a customer this call centre
            // failed to answer, and counting a customer who was out as one of
            // those would spoil the number the reports exist to produce.
            CallOutcome.NoAnswer => CommunicationStatuses.NoAnswer,
            CallOutcome.Failed => CommunicationStatuses.Failed,
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
