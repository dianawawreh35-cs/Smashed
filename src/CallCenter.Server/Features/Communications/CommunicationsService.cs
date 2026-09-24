using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Phone;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Communications;

/// <summary>
/// The record of every call (A-14). What the reports, the contact history and
/// the classification all hang off.
/// </summary>
/// <remarks>
/// Written by the Agent App as each call ends, and later by the CDR importer for
/// calls that never reached an agent (S-55). Both write the same table so every
/// report is one query.
///
/// <b>Reporting the same call twice is harmless.</b> A unique index on
/// (<c>sip_call_id</c>, <c>extension</c>) means a repeat is recognised and
/// updated rather than inserted, which is what lets the Agent App's offline
/// queue resend without first asking what already arrived. That matters more
/// than it sounds: the alternative is an app that has to reconcile, and an app
/// that has to reconcile will get it wrong on the day the network is flapping.
/// </remarks>
public class CommunicationsService(
    CallCenterDbContext db,
    Settings.SettingsService settings,
    CallEditWindow editWindow,
    RecordingStore recordings,
    ILogger<CommunicationsService> logger)
{
    /// <summary>
    /// How far back an agent's call log reaches when the supervisor has not set
    /// it (A-50, setting <c>agent.call_log_days</c>).
    /// </summary>
    public const int DefaultCallLogDays = 7;

    public enum Failure
    {
        /// <summary>The status or direction was not one this system knows.</summary>
        UnknownValue,

        /// <summary>The Phone channel is missing, which means the database was never seeded.</summary>
        NoPhoneChannel,

        /// <summary>No such call.</summary>
        NotFound,

        /// <summary>
        /// The call is not missed, rejected or unanswered. An answered call is
        /// classified instead, and a blocked or failed one takes nothing.
        /// </summary>
        NotesNotTaken,

        /// <summary>The call belongs to another agent.</summary>
        NotYours,

        /// <summary>The agent's window to edit their own call has closed (A-42).</summary>
        EditWindowClosed,
    }

    /// <summary>
    /// Records one call, or updates it if the app has reported it before (A-14).
    /// </summary>
    public async Task<(CommunicationDto? Communication, Failure? Failure)> LogCallAsync(
        LogCallRequest request, Guid agentId, CancellationToken ct = default)
    {
        if (!CommunicationStatuses.All.Contains(request.Status)
            || !Directions.All.Contains(request.Direction))
        {
            return (null, Failure.UnknownValue);
        }

        var channelId = await db.Channels
            .Where(c => c.Name == ChannelNames.Phone)
            .Select(c => c.Id)
            .FirstOrDefaultAsync(ct);

        if (channelId == Guid.Empty)
        {
            // Not a validation problem — the deployment is wrong. Worth its own
            // answer rather than a generic failure, because the fix is to seed.
            logger.LogError("No Phone channel exists; the database has not been seeded.");
            return (null, Failure.NoPhoneChannel);
        }

        var normalised = PhoneNormalizer.Normalize(request.RemoteNumber);

        var existing = await db.Communications
            .FirstOrDefaultAsync(
                c => c.SipCallId == request.SipCallId && c.Extension == request.Extension, ct);

        var call = existing ?? new Communication
        {
            Kind = CommunicationKinds.Call,
            ChannelId = channelId,
            SipCallId = request.SipCallId,
            Extension = request.Extension,
            Source = CommunicationSources.AgentApp,
        };

        call.Direction = request.Direction;
        call.Status = request.Status;

        // Nobody handled a blocked call — it was refused before anything rang —
        // but it is still this agent's laptop that saw it, so the agent is
        // recorded. Abandoned calls, which no laptop sees, are the ones with a
        // null agent (S-55).
        call.AgentId = agentId;

        call.RemoteNumberRaw = request.RemoteNumber;
        call.RemoteNormalised = string.IsNullOrEmpty(normalised) ? null : normalised;
        call.RemoteName = request.RemoteName;
        call.StartedAt = request.StartedAt;
        call.AnsweredAt = request.AnsweredAt;
        call.EndedAt = request.EndedAt;
        call.QueueName = request.Queue;
        call.LaptopId = request.LaptopId;

        // Talk time, not ring time: a call that rang for 40 seconds and was
        // never answered has no duration, and giving it one would put 40 seconds
        // of imaginary conversation into every average.
        call.DurationSec = request.AnsweredAt is { } answered && request.EndedAt is { } ended
            ? Math.Max(0, (int)(ended - answered).TotalSeconds)
            : null;

        // Matching is the server's job (A-13). The app never sends a contact id:
        // it would have to duplicate the matching rules to work one out, and two
        // copies of those rules is how a caller ends up on the wrong customer.
        call.ContactId = await MatchContactAsync(request.RemoteNumber, ct);

        call.UpdatedAt = DateTimeOffset.UtcNow;

        if (existing is null)
        {
            db.Communications.Add(call);
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Call {Status} logged for extension {Extension} ({Existing})",
            call.Status, call.Extension, existing is null ? "new" : "updated");

        return (await ToDtoAsync(call, ct), null);
    }

    /// <summary>
    /// Writes the note on a missed, rejected or unanswered outbound call — why
    /// it went that way (A-41). Blank clears it.
    /// </summary>
    /// <remarks>
    /// These calls are never classified: there was no conversation, so there is
    /// no order or complaint to record, and a classification would put one into
    /// the reports. What is worth keeping is the reason, and that is a sentence.
    ///
    /// Held to the same edit window as a classification (<see cref="CallEditWindow"/>).
    /// </remarks>
    public async Task<(CommunicationDto? Communication, Failure? Failure)> SaveNotesAsync(
        Guid communicationId,
        string? notes,
        Guid actingUserId,
        bool actorIsSupervisor,
        CancellationToken ct = default)
    {
        var call = await db.Communications.FirstOrDefaultAsync(c => c.Id == communicationId, ct);

        if (call is null)
        {
            return (null, Failure.NotFound);
        }

        if (!CommunicationStatuses.TakesNotes(call.Status))
        {
            return (null, Failure.NotesNotTaken);
        }

        switch (await editWindow.CheckAsync(call, actingUserId, actorIsSupervisor, ct))
        {
            case CallEditWindow.Refusal.NotYours:
                return (null, Failure.NotYours);
            case CallEditWindow.Refusal.Closed:
                return (null, Failure.EditWindowClosed);
        }

        call.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        call.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(ct);

        return (await ToDtoAsync(call, ct), null);
    }

    /// <summary>
    /// Writes a note on a call the server may not have been told about yet
    /// (A-04, A-41).
    /// </summary>
    /// <remarks>
    /// From the pop-up, keyed on the SIP Call-ID and extension. The Agent App
    /// queues the note behind its call, so normally the call is here; if it is
    /// not, the caller is told and the app keeps the note queued and tries
    /// again.
    /// </remarks>
    public async Task<(CommunicationDto? Communication, Failure? Failure)> SaveNotesByCallAsync(
        SaveCallNotesByCallRequest request,
        Guid actingUserId,
        bool actorIsSupervisor,
        CancellationToken ct = default)
    {
        var id = await db.Communications
            .Where(c => c.SipCallId == request.SipCallId && c.Extension == request.Extension)
            .OrderByDescending(c => c.StartedAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        return id is null
            ? (null, Failure.NotFound)
            : await SaveNotesAsync(id.Value, request.Notes, actingUserId, actorIsSupervisor, ct);
    }

    /// <summary>
    /// An agent's own calls, newest first, narrowed by date, text and whether
    /// they still need classifying (A-50).
    /// </summary>
    /// <remarks>
    /// <b>The filtering has to happen here, not in the app.</b> The first
    /// version fetched the last hundred calls and filtered them on the laptop,
    /// which is wrong in a way that is invisible: an agent searching for a
    /// customer they spoke to two hundred calls ago is told "no calls match",
    /// and a date filter would report an empty Tuesday. A screen that answers
    /// "nothing" when it means "nothing in the part I looked at" is worse than
    /// one that cannot answer at all.
    ///
    /// A busy agent takes fifty to a hundred calls a day, so a hundred rows is
    /// roughly one shift. Anything historical was always going to be outside it.
    ///
    /// <b>An agent sees a window, not the whole history.</b> How far back is
    /// <c>agent.call_log_days</c>, a week by default and set by the supervisor
    /// (S-47). Enforced here rather than by the screen, because a limit the
    /// client applies is not a limit: this method takes the caller's
    /// <paramref name="from"/> and moves it forward if it reaches past the
    /// window.
    ///
    /// It is a working window, not a retention rule. Nothing is deleted, the
    /// contact history still shows every call, and the supervisor's reports see
    /// all of it. What it bounds is the query behind one agent's screen — which
    /// is also what keeps that screen fast on a busy extension, since the window
    /// plus <c>ix_comm_agent_started</c> makes this a short range scan rather
    /// than a walk back through the year.
    /// </remarks>
    /// <param name="from">Inclusive, and clamped to the window the supervisor allows.</param>
    /// <param name="to">Inclusive of the whole day: the caller passes a date, not an instant.</param>
    /// <param name="query">Matches the number as dialled, the normalised number, or the contact's name.</param>
    /// <param name="unclassifiedOnly">Only answered calls with no classification (A-41).</param>
    public async Task<IReadOnlyList<CommunicationDto>> ForAgentAsync(
        Guid agentId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? query = null,
        bool unclassifiedOnly = false,
        int limit = 100,
        CancellationToken ct = default)
    {
        var days = await settings.GetIntAsync("agent.call_log_days", DefaultCallLogDays, ct);
        var earliest = DateTimeOffset.UtcNow.AddDays(-days);

        var calls = db.Communications.Where(c => c.AgentId == agentId);

        // The window always applies. A caller asking for more gets the window;
        // a caller asking for less gets what they asked for.
        var start = from is { } requested && requested > earliest ? requested : earliest;
        calls = calls.Where(c => c.StartedAt >= start);

        if (to is { } end)
        {
            // The caller means a day, not a moment: "to Tuesday" includes
            // Tuesday's calls, so the bound runs to the end of that day.
            var endOfDay = end.Date.AddDays(1).AddTicks(-1);
            calls = calls.Where(c => c.StartedAt <= endOfDay);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();
            var like = $"%{text}%";

            // The number as dialled and the normalised form both, so searching
            // "0599" finds a call stored as 970599… and vice versa.
            var digits = PhoneNormalizer.DigitsOnly(text);
            var digitsLike = digits.Length > 0 ? $"%{digits}%" : null;

            calls = calls.Where(c =>
                (c.RemoteNumberRaw != null && EF.Functions.ILike(c.RemoteNumberRaw, like))
                || (digitsLike != null && c.RemoteNormalised != null
                    && EF.Functions.ILike(c.RemoteNormalised, digitsLike))
                || (c.Contact != null && c.Contact.Name != null
                    && EF.Functions.ILike(c.Contact.Name, like)));
        }

        if (unclassifiedOnly)
        {
            // Answered calls only: a missed or blocked call has no conversation
            // to classify, so it can never be cleared off the list (A-41).
            calls = calls
                .Where(c => c.Status == CommunicationStatuses.Answered)
                .Where(c => !db.Classifications.Any(x => x.CommunicationId == c.Id));
        }

        // AsNoTracking: this is a read. Tracking would have EF build a change
        // snapshot of every row it returns, for a list nobody edits.
        //
        // The shape of the query is what keeps it quick on a busy extension:
        // ix_comm_agent_started is (agent_id, started_at DESC), which is exactly
        // the filter and exactly the sort, so PostgreSQL walks the index
        // backwards from the window's edge and stops after `limit` rows. It
        // never reads the year behind it.
        var rows = await calls
            .AsNoTracking()
            .OrderByDescending(c => c.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

        return await ToDtosAsync(rows, ct);
    }

    /// <summary>
    /// Stores a recording against the call it belongs to (A-31).
    /// </summary>
    /// <remarks>
    /// The call is found by the phone system's own reference plus the
    /// extension — the same pair a classification uses, and the same pair the
    /// Agent App has in hand. Recording and classification therefore hang off
    /// the call rather than off each other.
    ///
    /// <b>A call the server has not been told about yet is not an error.</b> The
    /// Agent App reports the call and uploads the recording as two separate
    /// items in one queue; if the upload somehow runs first, the answer is
    /// "not yet" and the app tries again. Any other outcome would either lose
    /// the recording or attach it to the wrong call.
    ///
    /// Re-uploading the same call replaces what is there. The offline queue
    /// resends, and a second row for one call would break the unique
    /// constraint and, worse, leave two files where one is silently orphaned.
    /// </remarks>
    public async Task<(Guid? Id, Failure? Failure)> SaveRecordingAsync(
        string sipCallId,
        string extension,
        Guid actingUserId,
        Stream audio,
        CancellationToken ct = default)
    {
        var call = await db.Communications
            .Where(c => c.SipCallId == sipCallId && c.Extension == extension)
            .OrderByDescending(c => c.StartedAt)
            .Select(c => new { c.Id, c.AgentId, c.StartedAt })
            .FirstOrDefaultAsync(ct);

        if (call is null)
        {
            logger.LogInformation(
                "A recording arrived for call {SipCallId} on {Extension}, which is not logged yet",
                sipCallId, extension);

            // NotFound maps to "call_not_found", which is what the Agent
            // App's queue defers on rather than discarding.
            return (null, Failure.NotFound);
        }

        // An agent may only file a recording against their own call. The token
        // decides, never the request.
        if (call.AgentId != actingUserId)
        {
            logger.LogWarning(
                "An agent tried to attach a recording to call {CallId}, which is not theirs", call.Id);

            return (null, Failure.NotYours);
        }

        var (path, size) = await recordings.SaveAsync(call.Id, call.StartedAt, audio, ct);

        var existing = await db.Recordings.FirstOrDefaultAsync(r => r.CommunicationId == call.Id, ct);

        if (existing is null)
        {
            db.Recordings.Add(new Recording
            {
                CommunicationId = call.Id,
                Path = path,
                SizeBytes = size,
                Format = "wav",
                UploadedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            existing.Path = path;
            existing.SizeBytes = size;
            existing.UploadedAt = DateTimeOffset.UtcNow;

            // A recording that was deleted by retention and then re-uploaded is
            // present again, so the marker has to go or it would read as gone.
            existing.DeletedAt = null;
        }

        await db.SaveChangesAsync(ct);

        return (call.Id, null);
    }

    /// <summary>
    /// Every communication for one contact, newest first — the history panel
    /// (A-62).
    /// </summary>
    public async Task<IReadOnlyList<CommunicationDto>> ForContactAsync(
        Guid contactId, int limit = 100, CancellationToken ct = default)
    {
        // ix_comm_contact is (contact_id, started_at DESC) - the same shape, so
        // the same short range scan. No time window here: A-62 asks for a
        // contact's full history, and one customer's calls are few enough that
        // the page limit is the only bound needed.
        var calls = await db.Communications
            .AsNoTracking()
            .Where(c => c.ContactId == contactId)
            .OrderByDescending(c => c.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

        return await ToDtosAsync(calls, ct);
    }

    /// <summary>
    /// The contact a number belongs to, by the same two steps as caller lookup
    /// (A-13): the normalised form, then the last nine digits.
    /// </summary>
    private async Task<Guid?> MatchContactAsync(string? number, CancellationToken ct)
    {
        var normalised = PhoneNormalizer.Normalize(number);
        if (string.IsNullOrEmpty(normalised))
        {
            return null;
        }

        var active = db.Contacts.Where(c => c.DeletedAt == null && c.MergedIntoId == null);

        var id = await active
            .Where(c => c.Phones.Any(p => p.Normalised == normalised))
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (id is not null)
        {
            return id;
        }

        var last9 = PhoneNormalizer.Last9(normalised);

        // Short numbers have no meaningful tail; every internal extension would
        // collide with every other.
        return last9.Length == 9
            ? await active
                .Where(c => c.Phones.Any(p => p.Last9 == last9))
                .Select(c => (Guid?)c.Id)
                .FirstOrDefaultAsync(ct)
            : null;
    }

    private async Task<CommunicationDto> ToDtoAsync(Communication call, CancellationToken ct) =>
        (await ToDtosAsync([call], ct))[0];

    /// <summary>
    /// Names for the contacts and agents in one pass, rather than a query per
    /// row: a call log of a hundred rows should not be a hundred round trips.
    /// </summary>
    private async Task<IReadOnlyList<CommunicationDto>> ToDtosAsync(
        IReadOnlyList<Communication> calls, CancellationToken ct)
    {
        var contactIds = calls.Where(c => c.ContactId is not null).Select(c => c.ContactId!.Value).Distinct().ToList();
        var agentIds = calls.Where(c => c.AgentId is not null).Select(c => c.AgentId!.Value).Distinct().ToList();
        var ids = calls.Select(c => c.Id).ToList();

        // Which of these have been classified (A-41). One query rather than a
        // navigation include: the classification itself is not wanted here, only
        // whether there is one.
        var classified = await db.Classifications
            .AsNoTracking()
            .Where(c => ids.Contains(c.CommunicationId))
            .Select(c => c.CommunicationId)
            .ToListAsync(ct);

        var classifiedSet = classified.ToHashSet();

        var contactNames = await db.Contacts
            .AsNoTracking()
            .Where(c => contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var agentNames = await db.Users
            .AsNoTracking()
            .Where(u => agentIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct);

        return calls.Select(c => new CommunicationDto(
            c.Id,
            c.Kind,
            c.Direction,
            c.Status,
            c.ContactId,
            c.ContactId is { } contactId && contactNames.TryGetValue(contactId, out var name) ? name : null,
            c.RemoteNumberRaw,
            c.RemoteName,
            c.StartedAt,
            c.AnsweredAt,
            c.EndedAt,
            c.DurationSec,
            c.QueueName,
            c.Extension,
            c.AgentId is { } agentId && agentNames.TryGetValue(agentId, out var agent) ? agent : null,
            classifiedSet.Contains(c.Id),
            c.Notes))
            .ToList();
    }
}
