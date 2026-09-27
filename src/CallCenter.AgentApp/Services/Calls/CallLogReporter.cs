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
///
/// <b>One bad item no longer stops the queue (F-08).</b> What the server refuses
/// for good, or keeps failing on while the items after it go through, is set
/// aside, and the queue carries on. The call log shows the agent how many.
/// </remarks>
public class CallLogReporter(
    ApiClient api,
    AgentSession session,
    CallLogQueue queue,
    AgentNotices notices,
    ILogger<CallLogReporter> logger)
{
    /// <summary>
    /// How many failures an item may have, each with something after it going
    /// through in the same pass, before it is set aside (F-08).
    /// </summary>
    public const int MaxAttempts = 5;

    /// <summary>
    /// After this many server errors in a row a pass stops: that is the server
    /// in trouble, not the items, and every item it touched would otherwise be
    /// marked down for it.
    /// </summary>
    private const int ServerErrorsToStop = 3;

    /// <summary>
    /// How long a classification, note or recording waits for a call that is
    /// not in the queue before it is set aside. Long, because the normal case
    /// is a form saved during a call that has not ended yet.
    /// </summary>
    private static readonly TimeSpan WaitForCall = TimeSpan.FromDays(1);

    /// <summary>What happened to something the agent saved (M-A03).</summary>
    public enum SaveOutcome
    {
        /// <summary>In the buffer; it goes when its turn comes.</summary>
        Queued,

        /// <summary>The buffer could not take it, and the server did.</summary>
        Sent,

        /// <summary>Neither. The agent must be told, and what they typed kept.</summary>
        Failed,
    }

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
            // delay the next call.
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
        if (await queue.EnqueueAsync(recording, ct))
        {
            await FlushAsync(ct);
            return;
        }

        // M-A03: the buffer could not take it, so once, directly. The file
        // stays on the laptop either way until the server has it (A-31).
        var sent = await api.UploadRecordingAsync(recording.SipCallId, recording.Extension, recording.LocalPath, ct);

        if (sent.IsOk)
        {
            DeleteLocal(recording.LocalPath);
            return;
        }

        logger.LogError(
            "A recording could not be queued or sent ({Code}); it stays at {Path}",
            sent.ErrorCode, recording.LocalPath);
        notices.Post("callLog.notKept");
    }

    /// <summary>
    /// Records a finished call. Never throws: a reporting problem must not reach
    /// the agent as a crash, and the one they can act on is said in the bar.
    /// </summary>
    public async Task ReportAsync(LogCallRequest call, CancellationToken ct = default)
    {
        if (await queue.EnqueueAsync(call, ct))
        {
            await FlushAsync(ct);
            return;
        }

        // M-A03: a call the buffer would not take used to be logged and then
        // forgotten. Now it is sent directly, once; if that fails too, the call
        // is gone, and the agent is told so a supervisor can know the reports
        // are one short.
        var sent = await api.LogCallAsync(call, ct);

        if (!sent.IsOk)
        {
            logger.LogError(
                "A call could not be queued or sent ({Code}): {SipCallId}, {Number}",
                sent.ErrorCode, call.SipCallId, call.RemoteNumber);
            notices.Post("callLog.notKept");
        }
    }

    /// <summary>
    /// Records what a call was about (A-40).
    /// </summary>
    /// <remarks>
    /// Queued rather than sent, always, even with the server right there. The
    /// form opens when the call is answered and the call is not reported until
    /// it ends, so a classification saved while the agent is still talking would
    /// arrive for a call the server has never heard of. The shared queue puts it
    /// behind its call; the flush sends it when its turn comes.
    ///
    /// <b>Returns once it is queued (M-A04)</b>, with the flush behind it. Save
    /// used to wait for the whole queue to go, which with the server down was
    /// ten seconds and more of an agent staring at a greyed button.
    /// </remarks>
    public async Task<SaveOutcome> ClassifyAsync(
        SaveClassificationByCallRequest classification, CancellationToken ct = default)
    {
        if (await queue.EnqueueAsync(classification, ct))
        {
            FlushInBackground();
            return SaveOutcome.Queued;
        }

        var sent = await api.ClassifyByCallAsync(classification, ct);

        if (sent.IsOk)
        {
            return SaveOutcome.Sent;
        }

        // During the call this is certain to fail - the server has no call to
        // attach it to yet - which is why it is only the fall-back.
        logger.LogError("A classification could not be queued or sent ({Code})", sent.ErrorCode);
        return SaveOutcome.Failed;
    }

    /// <summary>
    /// Records the note on a call that has just ended (A-41) — an outbound call
    /// the customer did not pick up. Returns once it is queued, as a
    /// classification does.
    /// </summary>
    public async Task<SaveOutcome> SaveNotesAsync(SaveCallNotesByCallRequest notes, CancellationToken ct = default)
    {
        if (await queue.EnqueueAsync(notes, ct))
        {
            FlushInBackground();
            return SaveOutcome.Queued;
        }

        var sent = await api.SaveCallNotesByCallAsync(notes, ct);

        if (sent.IsOk)
        {
            return SaveOutcome.Sent;
        }

        logger.LogError("A call note could not be queued or sent ({Code})", sent.ErrorCode);
        return SaveOutcome.Failed;
    }

    /// <summary>
    /// A flush nobody waits for (M-A04). Never throws; a failure is logged and
    /// the next call, the next minute or the next sign-in tries again.
    /// </summary>
    public void FlushInBackground() =>
        _ = Task.Run(async () =>
        {
            try
            {
                await FlushAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Sending the queued items failed");
            }
        });

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

    // ---- what has been set aside (F-08) -----------------------------------

    /// <summary>
    /// How many of the signed-in agent's items have been set aside. The call
    /// log's rail button shows it as a badge.
    /// </summary>
    public int SetAsideCount { get; private set; }

    /// <summary>Raised when <see cref="SetAsideCount"/> changes, on any thread.</summary>
    public event EventHandler? SetAsideChanged;

    /// <summary>The signed-in agent's set-aside items, for the call log's panel.</summary>
    public Task<IReadOnlyList<SetAsideItem>> SetAsideItemsAsync(CancellationToken ct = default) =>
        session.User is { } user
            ? queue.SetAsideItemsAsync(user.Id, ct)
            : Task.FromResult<IReadOnlyList<SetAsideItem>>([]);

    /// <summary>
    /// Puts the agent's set-aside items back on the queue and sends them now.
    /// For when the server's refusal has been dealt with.
    /// </summary>
    public async Task RetrySetAsideAsync(CancellationToken ct = default)
    {
        if (session.User is not { } user)
        {
            return;
        }

        var count = await queue.RetrySetAsideAsync(user.Id, ct);
        logger.LogInformation("{Count} set-aside item(s) put back on the queue by the agent", count);

        await FlushAsync(ct);
    }

    private async Task RefreshSetAsideAsync(CancellationToken ct)
    {
        var count = session.User is { } user ? (await queue.SetAsideItemsAsync(user.Id, ct)).Count : 0;

        if (count == SetAsideCount)
        {
            return;
        }

        SetAsideCount = count;
        SetAsideChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- the pass ----------------------------------------------------------

    /// <summary>
    /// Refusals the server is certain about. Retried, they fail the same way
    /// for ever, so they are set aside at once.
    /// </summary>
    /// <remarks>
    /// <c>extension_not_yours</c> is the server refusing a call logged under an
    /// extension that is not the signed-in agent's (F-03, server side, prompt
    /// 18). <c>bad_request</c> is a 400 that carried no code (F-08).
    /// </remarks>
    private static bool IsPermanent(string? code) => code is
        "unknown_value" or "invalid_request" or "unknown_type" or "unknown_branch" or "not_your_call"
        or "not_answered" or "notes_not_taken" or "edit_window_closed"
        or "extension_not_yours" or ApiClient.BadRequest;

    private async Task FlushPassAsync(CancellationToken ct)
    {
        if (!session.IsSignedIn || session.User is not { } user)
        {
            // Nothing can be sent without a token. The queue keeps until the
            // next sign-in, which is exactly what it is for.
            return;
        }

        // F-03: rows from before the owner was recorded are this agent's now,
        // once (Dia's call, 27 Sep). Nothing is unowned after this.
        var claimed = await queue.ClaimUnownedAsync(user.Id, ct);

        if (claimed > 0)
        {
            logger.LogWarning(
                "{Count} queued item(s) had no owner, from before 27 Sep; they are sent now as {Login} (F-03)",
                claimed, user.Login);
        }

        var pending = await queue.PendingItemsAsync(user.Id, ct);

        if (pending.Count == 0)
        {
            await RefreshSetAsideAsync(ct);
            return;
        }

        // Recordings last (F-08). A long one can take minutes to upload, and
        // the calls and forms behind it should not wait for it. Still after
        // their own call, which is in the first half.
        var ordered = pending.Where(i => i.Recording is null)
            .Concat(pending.Where(i => i.Recording is not null))
            .ToList();

        var callsQueued = pending.Where(i => i.Call is not null).Select(i => i.Reference).ToHashSet();

        var done = new List<long>();
        var setAside = new List<(long Id, string Reason)>();
        var callsSetAside = new List<string>();
        var failed = new List<(CallLogQueue.PendingItem Item, string? Code, int Index)>();

        // Calls that did not go through in this pass. What belongs to them
        // waits behind them rather than being sent to be refused.
        var held = new HashSet<string>();

        var lastSuccess = -1;
        var serverErrorsInARow = 0;

        // Set when a classification was skipped because its call had not been
        // sent yet. If a call did go out in this pass, one more pass clears it
        // rather than leaving it until the next call ends.
        var deferred = false;

        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];

            if (item.Call is null && item.Reference is { } waitsFor && held.Contains(waitsFor))
            {
                continue;
            }

            var (ok, status, errorCode) = await SendAsync(item, ct);

            if (ok)
            {
                done.Add(item.Id);
                lastSuccess = index;
                serverErrorsInARow = 0;
                continue;
            }

            // A classification, note or recording for a call the server does
            // not have yet.
            //
            // This is the normal case, not the exception, and the queue order is
            // the opposite of what it looks like: the agent saves the form while
            // still talking, so the classification is queued *before* the call,
            // which is not reported until hang-up. Its id is therefore lower
            // than its call's. Skipped and left in place, never a reason to stop.
            if (item.Call is null && errorCode is "call_not_found")
            {
                // Unless the call is not in the queue at all and has had a day
                // to arrive: then it is never coming, and this would wait for
                // it for ever.
                if (!callsQueued.Contains(item.Reference) && DateTimeOffset.Now - item.CreatedAt > WaitForCall)
                {
                    setAside.Add((item.Id, "call_never_arrived"));
                    continue;
                }

                logger.LogDebug(
                    "A classification, note or recording is waiting for its call to be reported ({Reference})",
                    item.Reference);

                deferred = true;
                continue;
            }

            if (IsPermanent(errorCode))
            {
                logger.LogError(
                    "The server refused queued item {Id} ({Code}); it is set aside rather than retried for ever",
                    item.Id, errorCode);

                setAside.Add((item.Id, errorCode!));

                if (item.Call is not null && item.Reference is { } reference)
                {
                    held.Add(reference);
                    callsSetAside.Add(reference);
                }

                continue;
            }

            if (status is ApiClient.ApiStatus.Unreachable or ApiClient.ApiStatus.Unauthorized)
            {
                // The server, not the item. Everything after would fail the
                // same way, so the pass stops, and the attempt does not count
                // against the item: an evening with the server down must not
                // wear the evening's calls out. A 401 is the sign-in ending
                // (N-05), which SignedOutByServer is already dealing with.
                await queue.RecordFailureAsync(item.Id, errorCode, counts: false, ct);

                logger.LogInformation(
                    "{Count} item(s) still waiting to reach the server ({Reason})",
                    ordered.Count - done.Count, errorCode ?? "unreachable");

                break;
            }

            // A server error, or an upload that ran out of time (F-08). Perhaps
            // this item, perhaps the server: skipped for now with whatever
            // belongs to it, and the pass carries on to the next.
            failed.Add((item, errorCode, index));

            if (item.Call is not null && item.Reference is { } failedCall)
            {
                held.Add(failedCall);
            }

            if (++serverErrorsInARow >= ServerErrorsToStop)
            {
                logger.LogWarning(
                    "The server failed {Count} items in a row ({Reason}); the rest wait for the next pass",
                    serverErrorsInARow, errorCode);
                break;
            }
        }

        if (done.Count > 0)
        {
            await queue.AcknowledgeAsync(done, ct);
            logger.LogInformation("{Count} item(s) reported to the server", done.Count);
        }

        // A failure counts against the item only if something after it went
        // through in the same pass: that is the server working, and refusing
        // this one. Failures after the last success say nothing either way.
        foreach (var (item, code, index) in failed)
        {
            var counts = index < lastSuccess;
            await queue.RecordFailureAsync(item.Id, code, counts, ct);

            if (counts && item.Attempts + 1 >= MaxAttempts)
            {
                logger.LogError(
                    "Queued item {Id} failed {Attempts} times while others went through ({Code}); it is set aside",
                    item.Id, item.Attempts + 1, code);

                setAside.Add((item.Id, "gave_up"));

                if (item.Call is not null && item.Reference is { } reference)
                {
                    callsSetAside.Add(reference);
                }
            }
        }

        if (setAside.Count > 0)
        {
            await queue.SetAsideAsync(setAside, ct);

            // What was waiting on a set-aside call can never go either.
            var followers = await queue.SetAsideFollowersAsync(user.Id, callsSetAside, ct);

            logger.LogError(
                "{Count} item(s) set aside, and {Followers} waiting on them; the call log shows them to the agent",
                setAside.Count, followers);
        }

        await RefreshSetAsideAsync(ct);

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

    /// <summary>Sends one queued item, whichever kind it is.</summary>
    private async Task<(bool Ok, ApiClient.ApiStatus Status, string? Code)> SendAsync(
        CallLogQueue.PendingItem item, CancellationToken ct)
    {
        if (item.Call is { } call)
        {
            var sent = await api.LogCallAsync(call, ct);
            return (sent.IsOk, sent.Status, sent.ErrorCode);
        }

        if (item.Classification is { } classification)
        {
            var sent = await api.ClassifyByCallAsync(classification, ct);
            return (sent.IsOk, sent.Status, sent.ErrorCode);
        }

        if (item.Notes is { } notes)
        {
            var sent = await api.SaveCallNotesByCallAsync(notes, ct);
            return (sent.IsOk, sent.Status, sent.ErrorCode);
        }

        var recording = item.Recording!;
        var uploaded = await api.UploadRecordingAsync(
            recording.SipCallId, recording.Extension, recording.LocalPath, ct);

        // A-31: the laptop's copy goes only once the server has it. A file
        // whose upload succeeded but whose confirmation was lost reports
        // "recording_missing" on the retry, which is also done.
        if (uploaded.IsOk || uploaded.ErrorCode is "recording_missing")
        {
            DeleteLocal(recording.LocalPath);
            return (true, ApiClient.ApiStatus.Ok, null);
        }

        return (false, uploaded.Status, uploaded.ErrorCode);
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
    /// <remarks>
    /// The number, the name and the queue are cut to what
    /// <see cref="LogCallRequest"/> accepts (F-08). They come from the PBX, and
    /// a name over 200 characters was refused 400 with no code, which the queue
    /// retried for ever. The Call-ID and the extension are left whole: they are
    /// the call's key, and a shortened one would be a different call.
    /// </remarks>
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
        RemoteNumber: Cut(call.Number, 40),
        RemoteName: Cut(call.CallerName, 200),
        StartedAt: call.StartedAt,
        AnsweredAt: call.AnsweredAt,
        EndedAt: call.EndedAt,
        Queue: Cut(call.Queue, 100),
        LaptopId: Cut(LaptopInfo.LaptopId, 100));

    /// <summary>At most <paramref name="max"/> characters; the same limits as the request's.</summary>
    private static string? Cut(string? value, int max) =>
        value is { Length: var length } && length > max ? value[..max] : value;
}
