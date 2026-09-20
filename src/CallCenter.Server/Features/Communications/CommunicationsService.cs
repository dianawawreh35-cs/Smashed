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
public class CommunicationsService(CallCenterDbContext db, ILogger<CommunicationsService> logger)
{
    public enum Failure
    {
        /// <summary>The status or direction was not one this system knows.</summary>
        UnknownValue,

        /// <summary>The Phone channel is missing, which means the database was never seeded.</summary>
        NoPhoneChannel,
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

    /// <summary>An agent's own calls, newest first (A-50).</summary>
    public async Task<IReadOnlyList<CommunicationDto>> ForAgentAsync(
        Guid agentId, int limit = 100, CancellationToken ct = default)
    {
        var calls = await db.Communications
            .Where(c => c.AgentId == agentId)
            .OrderByDescending(c => c.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

        return await ToDtosAsync(calls, ct);
    }

    /// <summary>
    /// Every communication for one contact, newest first — the history panel
    /// (A-62).
    /// </summary>
    public async Task<IReadOnlyList<CommunicationDto>> ForContactAsync(
        Guid contactId, int limit = 100, CancellationToken ct = default)
    {
        var calls = await db.Communications
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
            .Where(c => ids.Contains(c.CommunicationId))
            .Select(c => c.CommunicationId)
            .ToListAsync(ct);

        var classifiedSet = classified.ToHashSet();

        var contactNames = await db.Contacts
            .Where(c => contactIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        var agentNames = await db.Users
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
            classifiedSet.Contains(c.Id)))
            .ToList();
    }
}
