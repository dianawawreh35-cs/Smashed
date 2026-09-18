using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared.Contracts.Contacts;
using CallCenter.Shared.Phone;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Contacts;

/// <summary>
/// The VIP and Blocked flags (S-45). Supervisors set them; agents only read.
/// </summary>
/// <remarks>
/// Deliberately separate from <see cref="ContactsService"/>, which agents reach
/// through <see cref="ContactsController"/>. Keeping the flags in their own
/// service and controller is what makes "an agent cannot change a flag" a fact
/// about the routing table rather than a rule someone has to remember while
/// adding a field to the contact form.
///
/// The flags live on the <c>contacts</c> row — there is no separate flag table.
/// A flag is a property of a customer, and a customer is a contact; a bare
/// number with no name is still a contact, just one whose name is null.
/// </remarks>
public class ContactFlagsService(CallCenterDbContext db, ILogger<ContactFlagsService> logger)
{
    /// <summary>How many flag changes the history returns. A screen, not an export.</summary>
    public const int HistoryLimit = 50;

    /// <summary>Audit actions this service writes, matching <see cref="AuditLogEntry.Action"/>.</summary>
    private const string FlagAction = "flag";
    private const string UnflagAction = "unflag";

    private static readonly JsonSerializerOptions AuditJson = new() { PropertyNameCaseInsensitive = true };

    public enum Failure
    {
        NotFound,

        /// <summary>The number was blank once normalised, so nothing could be flagged.</summary>
        NoUsableNumber,

        /// <summary>
        /// VIP and Blocked at once. Refused rather than silently preferring one:
        /// the two ask the Agent App for opposite behaviour (A-16 shows a badge,
        /// A-17 rejects the call) and there is no sensible winner.
        /// </summary>
        VipAndBlocked,

        /// <summary>A flag was set with no reason given (S-45).</summary>
        ReasonRequired,
    }

    /// <summary>
    /// Every flagged contact — the list S-45 asks for. VIP first, then blocked,
    /// each by name.
    /// </summary>
    public async Task<IReadOnlyList<FlaggedContactDto>> ListAsync(CancellationToken ct = default) =>
        await Active()
            .Where(c => c.IsVip || c.IsBlocked)
            .OrderByDescending(c => c.IsVip)
            .ThenBy(c => c.Name)
            .Select(c => new FlaggedContactDto(
                c.Id,
                c.Name,
                c.Address,
                c.IsVip,
                c.IsBlocked,
                c.FlagReason,
                db.Users.Where(u => u.Id == c.FlagChangedBy).Select(u => u.DisplayName).FirstOrDefault(),
                c.FlagChangedAt,
                c.Phones.OrderByDescending(p => p.IsPrimary).Select(p => p.Raw).ToList()))
            .ToListAsync(ct);

    /// <summary>
    /// Every blocked number, normalised, for the Agent App's local cache (A-17).
    /// </summary>
    /// <remarks>
    /// Numbers rather than contacts, because that is all the rejection needs and
    /// it keeps customer names off every agent's disk. A blocked contact with
    /// three numbers contributes three entries: any of them calling is the same
    /// person.
    /// </remarks>
    public async Task<BlockedNumbersDto> BlockedNumbersAsync(CancellationToken ct = default)
    {
        var numbers = await Active()
            .Where(c => c.IsBlocked)
            .SelectMany(c => c.Phones.Select(p => p.Normalised))
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync(ct);

        return new BlockedNumbersDto(numbers, DateTimeOffset.UtcNow);
    }

    /// <summary>Sets, changes or removes a contact's flags (S-45).</summary>
    public async Task<(FlaggedContactDto? Contact, Failure? Failure)> SetAsync(
        Guid id, SetContactFlagsRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        if (Reject(request.IsVip, request.IsBlocked, request.Reason) is { } rejection)
        {
            return (null, rejection);
        }

        var contact = await Active().Include(c => c.Phones).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (contact is null)
        {
            return (null, Failure.NotFound);
        }

        await ApplyAsync(contact, request.IsVip, request.IsBlocked, request.Reason, actingUserId, ct);

        return (await ToDtoAsync(contact, ct), null);
    }

    /// <summary>
    /// Flags a bare number (S-45), attaching the flag to the contact that
    /// already owns the number where there is one.
    /// </summary>
    public async Task<(FlaggedContactDto? Contact, Failure? Failure)> FlagNumberAsync(
        FlagNumberRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        if (Reject(request.IsVip, request.IsBlocked, request.Reason) is { } rejection)
        {
            return (null, rejection);
        }

        var normalised = PhoneNormalizer.Normalize(request.Number);
        if (string.IsNullOrEmpty(normalised))
        {
            return (null, Failure.NoUsableNumber);
        }

        var contact = await FindByNumberAsync(request.Number, normalised, ct);

        if (contact is null)
        {
            // Nothing on file. A nameless contact carries the flag, so a nuisance
            // number and a customer are the same kind of row and the pop-up's
            // lookup (A-13) finds both with the same query.
            contact = new Contact
            {
                CreatedBy = actingUserId,
                UpdatedBy = actingUserId,
            };

            contact.Phones.Add(new ContactPhone
            {
                Raw = request.Number.Trim(),
                Normalised = normalised,
                IsPrimary = true,
            });

            db.Contacts.Add(contact);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Contact {ContactId} created to carry a flag on a bare number", contact.Id);
        }

        await ApplyAsync(contact, request.IsVip, request.IsBlocked, request.Reason, actingUserId, ct);

        return (await ToDtoAsync(contact, ct), null);
    }

    /// <summary>
    /// Every flag change made to this contact, newest first (S-45, N-06), read
    /// back out of the audit log.
    /// </summary>
    public async Task<IReadOnlyList<ContactFlagChangeDto>> HistoryAsync(
        Guid id, CancellationToken ct = default)
    {
        var entityId = id.ToString();

        var entries = await db.AuditLog
            .Where(e => e.Entity == "contact" && e.EntityId == entityId)
            .Where(e => e.Action == FlagAction || e.Action == UnflagAction)
            .OrderByDescending(e => e.Id)
            .Take(HistoryLimit)
            .Select(e => new
            {
                e.At,
                e.After,
                ByDisplayName = db.Users.Where(u => u.Id == e.UserId)
                    .Select(u => u.DisplayName).FirstOrDefault(),
            })
            .ToListAsync(ct);

        return entries
            .Select(e =>
            {
                var state = e.After is null
                    ? null
                    : e.After.Deserialize<FlagState>(AuditJson);

                return new ContactFlagChangeDto(
                    e.At, e.ByDisplayName, state?.IsVip ?? false, state?.IsBlocked ?? false, state?.Reason);
            })
            .ToList();
    }

    /// <summary>
    /// Contacts that are neither merged away nor deleted — the same rule
    /// <see cref="ContactsService"/> applies, so a flag can never be hidden on a
    /// contact that no longer appears anywhere.
    /// </summary>
    private IQueryable<Contact> Active() =>
        db.Contacts.Where(c => c.DeletedAt == null && c.MergedIntoId == null);

    /// <summary>
    /// What a request is refused for, before anything is read. In one place so
    /// both entry points refuse the same things.
    /// </summary>
    private static Failure? Reject(bool isVip, bool isBlocked, string? reason)
    {
        if (isVip && isBlocked)
        {
            return Failure.VipAndBlocked;
        }

        // Removing both flags needs no reason: the removal itself is recorded
        // with who and when, which is what a later reader wants to know.
        if ((isVip || isBlocked) && string.IsNullOrWhiteSpace(reason))
        {
            return Failure.ReasonRequired;
        }

        return null;
    }

    /// <summary>
    /// The contact a number belongs to: exact normalised match, then the last
    /// nine digits — the same two steps as caller lookup (A-13), so flagging a
    /// number and a call from it always reach the same contact.
    /// </summary>
    private async Task<Contact?> FindByNumberAsync(string raw, string normalised, CancellationToken ct)
    {
        var contact = await Active()
            .Include(c => c.Phones)
            .FirstOrDefaultAsync(c => c.Phones.Any(p => p.Normalised == normalised), ct);

        if (contact is not null)
        {
            return contact;
        }

        var last9 = PhoneNormalizer.Last9(raw);

        // Short numbers have no meaningful tail; every internal extension would
        // collide, and blocking one by accident would be expensive.
        return last9.Length == 9
            ? await Active().Include(c => c.Phones)
                .FirstOrDefaultAsync(c => c.Phones.Any(p => p.Last9 == last9), ct)
            : null;
    }

    /// <summary>Writes the new state and the audit entry for it, in one save.</summary>
    private async Task ApplyAsync(
        Contact contact, bool isVip, bool isBlocked, string? reason, Guid actingUserId, CancellationToken ct)
    {
        var before = Snapshot(contact);

        contact.IsVip = isVip;
        contact.IsBlocked = isBlocked;

        // The reason belongs to the flag, so clearing the flags clears it too: a
        // stale "abusive on the phone" next to an unflagged contact reads as if
        // they were still blocked.
        contact.FlagReason = isVip || isBlocked ? reason?.Trim() : null;

        contact.FlagChangedBy = actingUserId;
        contact.FlagChangedAt = DateTimeOffset.UtcNow;
        contact.UpdatedBy = actingUserId;
        contact.UpdatedAt = DateTimeOffset.UtcNow;

        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = actingUserId,
            Entity = "contact",
            EntityId = contact.Id.ToString(),
            Action = isVip || isBlocked ? FlagAction : UnflagAction,
            Before = before,
            After = Snapshot(contact),
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Contact {ContactId} flags set to VIP={IsVip} Blocked={IsBlocked} by {UserId}",
            contact.Id, isVip, isBlocked, actingUserId);
    }

    private async Task<FlaggedContactDto> ToDtoAsync(Contact contact, CancellationToken ct)
    {
        var changedBy = contact.FlagChangedBy is null
            ? null
            : await db.Users.Where(u => u.Id == contact.FlagChangedBy)
                .Select(u => u.DisplayName)
                .FirstOrDefaultAsync(ct);

        return new FlaggedContactDto(
            contact.Id,
            contact.Name,
            contact.Address,
            contact.IsVip,
            contact.IsBlocked,
            contact.FlagReason,
            changedBy,
            contact.FlagChangedAt,
            contact.Phones.OrderByDescending(p => p.IsPrimary).Select(p => p.Raw).ToList());
    }

    private static JsonDocument Snapshot(Contact contact) =>
        JsonSerializer.SerializeToDocument(new FlagState(contact.IsVip, contact.IsBlocked, contact.FlagReason));

    /// <summary>
    /// What a flag audit entry stores. A named type rather than an anonymous
    /// one because <see cref="HistoryAsync"/> has to read it back, and the two
    /// shapes must not be able to drift apart.
    /// </summary>
    private record FlagState(bool IsVip, bool IsBlocked, string? Reason);
}
