using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Classifications;
using CallCenter.Server.Features.Communications;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Phone;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Applications;

/// <summary>
/// Messages: the conversations that reach the restaurant on WhatsApp,
/// Facebook, Instagram, Wheels and the other apps, recorded by the agent who
/// handled them (A-70, A-71).
/// </summary>
/// <remarks>
/// <b>A message is a <c>communications</c> row</b> with <c>kind = App</c>,
/// <c>direction = None</c> and <c>status = Logged</c>, filed under the app's
/// channel. Not a table of its own: the schema was designed for this on 14
/// September, and one table is what makes a contact's history show calls and
/// messages together (A-72) and a combined report a single query. The three
/// values were declared then and first written here.
///
/// <b>The agent types it, so nothing is queued.</b> A call happens whether or
/// not the server is up, which is why calls queue (A-04). A message is written
/// after the fact by an agent who can wait a minute; refusing plainly and
/// keeping what was typed is simpler and cannot lose or duplicate anything
/// (Dia, 25 Sep).
///
/// <b>Never deleted.</b> The same rule as calls: an agent edits their own within
/// the window (<see cref="CallEditWindow"/>), a supervisor always, and a wrong
/// entry becomes <i>Other</i> with a note (Dia, 25 Sep).
/// </remarks>
public class ApplicationsService(
    CallCenterDbContext db,
    CommunicationsService communications,
    ClassificationService classifications,
    CallEditWindow editWindow,
    ILogger<ApplicationsService> logger)
{
    /// <summary>
    /// How far into the future a message's time may be and still count as
    /// "now": the agent's clock against the server's.
    /// </summary>
    private static readonly TimeSpan ClockSlack = TimeSpan.FromMinutes(5);

    public enum Failure
    {
        /// <summary>No such channel, or it has been hidden.</summary>
        UnknownChannel,

        /// <summary>Phone. A phone conversation is a call, and calls are logged by the Agent App as they end.</summary>
        PhoneChannel,

        /// <summary>Neither a number nor a contact was given, so nobody is on the other end.</summary>
        NoCustomer,

        /// <summary>No such contact, or it was merged away or deleted.</summary>
        UnknownContact,

        /// <summary>
        /// In the future, or on another day than today for an agent. An agent
        /// may set the time back within their day so the message keeps the
        /// hour the customer wrote; other days are the supervisor's.
        /// </summary>
        BadTime,

        /// <summary>No such message.</summary>
        NotFound,

        /// <summary>The row is a call. Calls are not edited here.</summary>
        NotAnApplication,

        /// <summary>The message belongs to another agent.</summary>
        NotYours,

        /// <summary>The agent's window to edit their own message has closed (A-42).</summary>
        EditWindowClosed,

        /// <summary>The classification sent with it was refused; <see cref="Outcome.ClassificationFailure"/> says why.</summary>
        Classification,
    }

    public sealed record Outcome(
        CommunicationDto? Message,
        Failure? Failure,
        ClassificationService.Failure? ClassificationFailure = null);

    /// <summary>
    /// Records a message, and classifies it in the same transaction when the
    /// agent filled the form (A-70).
    /// </summary>
    /// <remarks>
    /// One request rather than two, so a message is never left half-recorded:
    /// if the classification is refused (a hidden type, a branch that does not
    /// exist) the message is not written either, and the agent sees why with
    /// everything still on screen. A message recorded without a classification
    /// is simply unclassified, as a skipped call is (A-41), and the agent's list
    /// and the per-agent report both show it.
    /// </remarks>
    public async Task<Outcome> RecordAsync(
        RecordApplicationRequest request, Guid actingUserId, bool actorIsSupervisor,
        CancellationToken ct = default)
    {
        var channel = await ChannelAsync(request.ChannelId!.Value, ct);
        if (channel.Failure is not null)
        {
            return new Outcome(null, channel.Failure);
        }

        var customer = await CustomerAsync(request.ContactId, request.RemoteNumber, ct);
        if (customer.Failure is not null)
        {
            return new Outcome(null, customer.Failure);
        }

        var now = DateTimeOffset.UtcNow;
        var startedAt = request.StartedAt ?? now;

        if (!TimeIsAllowed(startedAt, now, actorIsSupervisor))
        {
            return new Outcome(null, Failure.BadTime);
        }

        var message = new Communication
        {
            // A-70: the three values the schema reserved for this.
            Kind = CommunicationKinds.App,
            Direction = Directions.None,
            Status = CommunicationStatuses.Logged,
            ChannelId = channel.Id,
            AgentId = actingUserId,
            ContactId = customer.ContactId,
            RemoteNumberRaw = customer.Number,
            RemoteNormalised = customer.Normalised,
            StartedAt = startedAt,
            Source = CommunicationSources.Manual,
            LaptopId = request.LaptopId,
            UpdatedAt = now,
        };

        // Both rows or neither: see the remarks. Through the retrying strategy,
        // which is the only way it lets a transaction be opened by hand.
        var refused = await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            db.Communications.Add(message);
            await db.SaveChangesAsync(ct);

            if (request.Classification is { } classification)
            {
                var (_, failure) = await classifications.SaveAsync(
                    message.Id, classification, actingUserId, actorIsSupervisor, ct);

                if (failure is not null)
                {
                    await transaction.RollbackAsync(ct);
                    db.ChangeTracker.Clear();
                    return failure;
                }
            }

            await transaction.CommitAsync(ct);
            return null;
        });

        if (refused is not null)
        {
            logger.LogInformation(
                "A message on {Channel} was not recorded: its classification was refused ({Reason})",
                channel.Name, refused);

            return new Outcome(null, Failure.Classification, refused);
        }

        logger.LogInformation(
            "Message recorded on {Channel} by {UserId} ({Classified})",
            channel.Name, actingUserId, request.Classification is null ? "unclassified" : "classified");

        return new Outcome(await communications.ToDtoAsync(message, ct), null);
    }

    /// <summary>
    /// Changes a message's channel, customer or time (A-71). What it was about
    /// is changed through the classification endpoints, as for a call.
    /// </summary>
    public async Task<Outcome> EditAsync(
        Guid id, EditApplicationRequest request, Guid actingUserId, bool actorIsSupervisor,
        CancellationToken ct = default)
    {
        var message = await db.Communications.FirstOrDefaultAsync(c => c.Id == id, ct);

        if (message is null)
        {
            return new Outcome(null, Failure.NotFound);
        }

        if (message.Kind != CommunicationKinds.App)
        {
            return new Outcome(null, Failure.NotAnApplication);
        }

        switch (await editWindow.CheckAsync(message, actingUserId, actorIsSupervisor, ct))
        {
            case CallEditWindow.Refusal.NotYours:
                return new Outcome(null, Failure.NotYours);
            case CallEditWindow.Refusal.Closed:
                return new Outcome(null, Failure.EditWindowClosed);
        }

        var channel = await ChannelAsync(request.ChannelId!.Value, ct);
        if (channel.Failure is not null)
        {
            return new Outcome(null, channel.Failure);
        }

        var customer = await CustomerAsync(request.ContactId, request.RemoteNumber, ct);
        if (customer.Failure is not null)
        {
            return new Outcome(null, customer.Failure);
        }

        var now = DateTimeOffset.UtcNow;
        var startedAt = request.StartedAt ?? message.StartedAt;

        // Only a changed time is judged. With the window set to Always an agent
        // may fix yesterday's channel, and the time it already has is not theirs
        // to defend.
        if (startedAt != message.StartedAt && !TimeIsAllowed(startedAt, now, actorIsSupervisor))
        {
            return new Outcome(null, Failure.BadTime);
        }

        message.ChannelId = channel.Id;
        message.ContactId = customer.ContactId;
        message.RemoteNumberRaw = customer.Number;
        message.RemoteNormalised = customer.Normalised;
        message.StartedAt = startedAt;
        message.UpdatedAt = now;

        await db.SaveChangesAsync(ct);

        return new Outcome(await communications.ToDtoAsync(message, ct), null);
    }

    /// <summary>
    /// An agent's own messages, newest first (A-71). Only theirs: the endpoint
    /// takes no agent id, the token decides.
    /// </summary>
    /// <remarks>
    /// Filtered here, never on the laptop (20 Sep, "filtering a page lies").
    /// The same working window as the call log, <c>agent.call_log_days</c>, and
    /// for the same reason: what bounds one agent's screen is not a retention
    /// rule, and the supervisor's pages see everything.
    /// </remarks>
    /// <param name="from">Inclusive, and clamped to the window.</param>
    /// <param name="to">Inclusive of the whole day: a date, not an instant.</param>
    /// <param name="query">Part of the number or of the contact's name.</param>
    public async Task<IReadOnlyList<CommunicationDto>> ForAgentAsync(
        Guid agentId,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        string? query = null,
        int limit = 100,
        CancellationToken ct = default)
    {
        var days = await communications.CallLogDaysAsync(ct);
        var earliest = DateTimeOffset.UtcNow.AddDays(-days);
        var start = from is { } requested && requested > earliest ? requested : earliest;

        var messages = db.Communications
            .Where(c => c.AgentId == agentId)
            .Where(c => c.Kind == CommunicationKinds.App)
            .Where(c => c.StartedAt >= start);

        if (to is { } end)
        {
            var endOfDay = end.Date.AddDays(1).AddTicks(-1);
            messages = messages.Where(c => c.StartedAt <= endOfDay);
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var text = query.Trim();
            var like = $"%{text}%";
            var digits = PhoneNormalizer.DigitsOnly(text);
            var digitsLike = digits.Length > 0 ? $"%{digits}%" : null;

            messages = messages.Where(c =>
                (c.RemoteNumberRaw != null && EF.Functions.ILike(c.RemoteNumberRaw, like))
                || (digitsLike != null && c.RemoteNormalised != null
                    && EF.Functions.ILike(c.RemoteNormalised, digitsLike))
                || (c.Contact != null && c.Contact.Name != null
                    && EF.Functions.ILike(c.Contact.Name, like)));
        }

        var rows = await messages
            .AsNoTracking()
            .OrderByDescending(c => c.StartedAt)
            .Take(limit)
            .ToListAsync(ct);

        return await communications.ToDtosAsync(rows, ct);
    }

    // ---- the pieces ---------------------------------------------------------

    private sealed record ChannelCheck(Guid Id, string Name, Failure? Failure);

    /// <summary>An app channel the supervisor still offers. Phone is refused: a phone conversation is a call.</summary>
    private async Task<ChannelCheck> ChannelAsync(Guid channelId, CancellationToken ct)
    {
        var channel = await db.Channels
            .AsNoTracking()
            .Where(c => c.Id == channelId)
            .Select(c => new { c.Name, c.IsSystem, c.IsActive })
            .FirstOrDefaultAsync(ct);

        return channel switch
        {
            null => new ChannelCheck(channelId, string.Empty, Failure.UnknownChannel),
            { IsSystem: true } => new ChannelCheck(channelId, channel.Name, Failure.PhoneChannel),
            { IsActive: false } => new ChannelCheck(channelId, channel.Name, Failure.UnknownChannel),
            _ => new ChannelCheck(channelId, channel.Name, null),
        };
    }

    private sealed record CustomerCheck(Guid? ContactId, string? Number, string? Normalised, Failure? Failure);

    /// <summary>
    /// Who the message was with. A chosen contact wins; otherwise the number is
    /// matched the way the pop-up matches a caller (A-13), and an unknown
    /// number is kept as typed with no contact, as an unknown caller is.
    /// </summary>
    private async Task<CustomerCheck> CustomerAsync(Guid? contactId, string? number, CancellationToken ct)
    {
        number = string.IsNullOrWhiteSpace(number) ? null : number.Trim();

        if (contactId is { } chosen)
        {
            var contact = await db.Contacts
                .AsNoTracking()
                .Where(c => c.Id == chosen && c.DeletedAt == null && c.MergedIntoId == null)
                .Select(c => new
                {
                    Primary = c.Phones.OrderByDescending(p => p.IsPrimary).Select(p => p.Raw).FirstOrDefault(),
                })
                .FirstOrDefaultAsync(ct);

            if (contact is null)
            {
                return new CustomerCheck(null, null, null, Failure.UnknownContact);
            }

            // The number the agent typed, or the contact's own when they typed
            // none: some apps never show one, and the row should still say who.
            number ??= contact.Primary;
            var normalisedChosen = PhoneNormalizer.Normalize(number);

            return new CustomerCheck(
                chosen, number, string.IsNullOrEmpty(normalisedChosen) ? null : normalisedChosen, null);
        }

        if (number is null)
        {
            return new CustomerCheck(null, null, null, Failure.NoCustomer);
        }

        var normalised = PhoneNormalizer.Normalize(number);

        return new CustomerCheck(
            await communications.MatchContactAsync(number, ct),
            number,
            string.IsNullOrEmpty(normalised) ? null : normalised,
            null);
    }

    /// <summary>
    /// Not in the future, and for an agent on today's date — local time, the
    /// same day the edit window is measured in (A-42). Dia, 25 Sep: the time
    /// defaults to now and an agent may set it back within the day.
    /// </summary>
    private static bool TimeIsAllowed(DateTimeOffset startedAt, DateTimeOffset now, bool actorIsSupervisor)
    {
        if (startedAt > now + ClockSlack)
        {
            return false;
        }

        return actorIsSupervisor || startedAt.ToLocalTime().Date == now.ToLocalTime().Date;
    }
}
