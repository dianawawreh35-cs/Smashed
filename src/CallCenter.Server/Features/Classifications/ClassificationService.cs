using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Data.Seed;
using CallCenter.Server.Features.Communications;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Classifications;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Classifications;

/// <summary>
/// What each call was about (A-40 to A-43, S-40).
/// </summary>
/// <remarks>
/// This is the feature the reports are built on: nearly every number the
/// supervisor asks for — how many orders, how many complaints, which branch,
/// what the complaints were about — is a count of classifications. Until it
/// existed, every call in the system was marked "unclassified" for ever and the
/// reports had nothing to count.
///
/// <b>The form is versioned, never edited in place.</b> A supervisor changing
/// the questions (S-40) publishes a new version; existing classifications keep
/// the version they were captured under, so a complaint classified in January
/// still reads back with the questions that were asked in January. Rewriting
/// history to match today's form would quietly change what an agent recorded.
///
/// <b>Every change is kept</b> (A-43), before and after, with who and when.
/// </remarks>
public class ClassificationService(
    CallCenterDbContext db,
    CallEditWindow editWindow,
    ILogger<ClassificationService> logger)
{
    public enum Failure
    {
        /// <summary>No such call.</summary>
        CommunicationNotFound,

        /// <summary>The call was not answered, so there is nothing to classify.</summary>
        NotAnswered,

        /// <summary>No such classification type, or it has been hidden.</summary>
        UnknownType,

        /// <summary>No such branch.</summary>
        UnknownBranch,

        /// <summary>The agent's window to edit their own call has closed (A-42).</summary>
        EditWindowClosed,

        /// <summary>The call belongs to another agent.</summary>
        NotYours,

        /// <summary>A type that is in use cannot be deleted, only hidden.</summary>
        TypeInUse,

        /// <summary>The form definition is not a shape this system can draw.</summary>
        BadForm,

        /// <summary>The name is missing, or already taken.</summary>
        BadName,
    }

    // ---- the form ----------------------------------------------------------

    /// <summary>
    /// The current form for one direction, its types and the branches, in one
    /// response (A-40).
    /// </summary>
    /// <remarks>
    /// One call rather than three. The Agent App fetches this at sign-in and
    /// keeps it; it must not be making three requests while an agent waits to
    /// classify the call they have just finished.
    ///
    /// Inbound and outbound calls have separate forms: a call the agent placed
    /// is a different conversation (a call-back, a confirmation, a follow-up)
    /// from an order coming in, and asking for a branch and an order value on
    /// it was noise. The types are shared, because the reports count by type
    /// whichever way the call went.
    /// </remarks>
    public async Task<ClassificationFormDto> FormAsync(
        string direction = Directions.In, CancellationToken ct = default)
    {
        direction = NormaliseDirection(direction);

        var form = await db.FormDefinitions
            .AsNoTracking()
            .Where(f => f.IsCurrent && f.Direction == direction)
            .OrderByDescending(f => f.Version)
            .FirstOrDefaultAsync(ct);

        if (form is null)
        {
            // A database seeded before the form existed, or one where the
            // current flag was lost. The agent still gets a usable form rather
            // than a broken screen.
            logger.LogError(
                "No current {Direction} form definition; falling back to the built-in fields", direction);
        }

        var typesInUse = await db.Classifications
            .Select(c => c.TypeId)
            .Distinct()
            .ToListAsync(ct);

        var types = await db.ClassificationTypes
            .AsNoTracking()
            .OrderBy(t => t.SortOrder).ThenBy(t => t.LabelEn)
            .Select(t => new ClassificationTypeDto(
                t.Id, t.Name, t.LabelAr, t.LabelEn, t.Colour,
                t.IsSystem, t.SortOrder, t.IsActive, typesInUse.Contains(t.Id)))
            .ToListAsync(ct);

        var branches = await db.Branches
            .AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Name)
            .Select(b => new FormBranchDto(b.Id, b.Name))
            .ToListAsync(ct);

        return new ClassificationFormDto(
            form?.Version ?? 1,
            form?.Definition ?? DefaultForm(direction),
            types,
            branches,
            direction);
    }

    /// <summary>
    /// <see cref="Directions.Out"/> when that is what was asked for, otherwise
    /// <see cref="Directions.In"/>. There is no form for app entries.
    /// </summary>
    public static string NormaliseDirection(string? direction) =>
        string.Equals(direction, Directions.Out, StringComparison.OrdinalIgnoreCase)
            ? Directions.Out
            : Directions.In;

    /// <summary>
    /// Publishes a new version of the form (S-40).
    /// </summary>
    /// <remarks>
    /// A new row every time, with the current flag moved to it. Nothing is
    /// overwritten, so a classification can always be read back against the
    /// questions it was captured under, and a supervisor who breaks the form can
    /// be put back by re-publishing an older definition.
    ///
    /// The change reaches agents without reinstalling anything: the Agent App
    /// asks for the form and notices the version has moved.
    /// </remarks>
    public async Task<(ClassificationFormDto? Form, Failure? Failure)> PublishFormAsync(
        JsonDocument definition, Guid actingUserId, string direction = Directions.In,
        CancellationToken ct = default)
    {
        direction = NormaliseDirection(direction);

        if (!IsDrawableForm(definition, out var reason))
        {
            logger.LogWarning("Refused a {Direction} form definition: {Reason}", direction, reason);
            return (null, Failure.BadForm);
        }

        // Versions are numbered across both directions: a classification
        // points at a version, and one sequence means the number alone says
        // which questions were asked.
        var nextVersion = await db.FormDefinitions.MaxAsync(f => (int?)f.Version, ct) ?? 0;

        // The old current is cleared first and saved separately: the partial
        // unique index allows only one current row per direction, and setting
        // the new one before clearing the old would collide.
        await db.FormDefinitions
            .Where(f => f.IsCurrent && f.Direction == direction)
            .ExecuteUpdateAsync(set => set.SetProperty(f => f.IsCurrent, false), ct);

        db.FormDefinitions.Add(new FormDefinition
        {
            Version = nextVersion + 1,
            Definition = definition,
            Direction = direction,
            IsCurrent = true,
            CreatedBy = actingUserId,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Classification form version {Version} ({Direction}) published by {UserId}",
            nextVersion + 1, direction, actingUserId);

        return (await FormAsync(direction, ct), null);
    }

    /// <summary>
    /// Whether a definition is one the two apps can actually draw.
    /// </summary>
    /// <remarks>
    /// Checked here rather than trusted, because a form that cannot be drawn
    /// stops every agent classifying every call until somebody notices. The
    /// rules are deliberately few: a list of fields, each with a key and a kind
    /// the clients know, and no two fields sharing a key.
    /// </remarks>
    private static bool IsDrawableForm(JsonDocument definition, out string reason)
    {
        reason = string.Empty;

        if (definition.RootElement.ValueKind != JsonValueKind.Object
            || !definition.RootElement.TryGetProperty("fields", out var fields)
            || fields.ValueKind != JsonValueKind.Array)
        {
            reason = "there is no fields array";
            return false;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var field in fields.EnumerateArray())
        {
            if (field.ValueKind != JsonValueKind.Object
                || !field.TryGetProperty("key", out var key)
                || key.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(key.GetString()))
            {
                reason = "a field has no key";
                return false;
            }

            if (!keys.Add(key.GetString()!))
            {
                reason = $"the key {key.GetString()} is used twice";
                return false;
            }

            if (!field.TryGetProperty("kind", out var kind)
                || kind.ValueKind != JsonValueKind.String
                || !FormFieldKinds.Contains(kind.GetString()!))
            {
                reason = $"the field {key.GetString()} has no kind this system can draw";
                return false;
            }
        }

        // The type is what every report groups by, and a form without it would
        // let a call be classified as nothing in particular.
        if (!keys.Contains("type"))
        {
            reason = "the form has no type field";
            return false;
        }

        return true;
    }

    /// <summary>The field kinds both apps know how to draw.</summary>
    private static readonly HashSet<string> FormFieldKinds = new(StringComparer.OrdinalIgnoreCase)
    {
        "type", "branch", "text", "textarea", "number", "select", "checkbox",
    };

    /// <summary>The fields the system was built around, for a database with no form row.</summary>
    private static JsonDocument DefaultForm(string direction) => JsonDocument.Parse(
        direction == Directions.Out ? SeedData.FormDefinitionOutV1 : SeedData.FormDefinitionV1);

    // ---- classifying -------------------------------------------------------

    /// <summary>
    /// Classifies a call, or edits what is already there (A-40, A-42, A-43).
    /// </summary>
    public async Task<(ClassificationDto? Saved, Failure? Failure)> SaveAsync(
        Guid communicationId,
        SaveClassificationRequest request,
        Guid actingUserId,
        bool actorIsSupervisor,
        CancellationToken ct = default)
    {
        var communication = await db.Communications
            .Include(c => c.Classification)
            .FirstOrDefaultAsync(c => c.Id == communicationId, ct);

        if (communication is null)
        {
            return (null, Failure.CommunicationNotFound);
        }

        // Only an answered call is classified. A missed, rejected or unanswered call takes a
        // note instead (CommunicationsService.SaveNotesAsync); classifying it
        // would put an "order" or a "complaint" into the reports for a call on
        // which nobody spoke.
        if (communication.Status != CommunicationStatuses.Answered)
        {
            return (null, Failure.NotAnswered);
        }

        var allowed = await MayEditAsync(communication, actingUserId, actorIsSupervisor, ct);
        if (allowed is not null)
        {
            return (null, allowed);
        }

        var type = await db.ClassificationTypes
            .FirstOrDefaultAsync(t => t.Id == request.TypeId!.Value, ct);

        // An inactive type may still be read back on old calls, but must not be
        // put on a new one - hiding a type is how a supervisor retires it.
        if (type is null || !type.IsActive)
        {
            return (null, Failure.UnknownType);
        }

        if (request.BranchId is { } branchId
            && !await db.Branches.AnyAsync(b => b.Id == branchId, ct))
        {
            return (null, Failure.UnknownBranch);
        }

        var before = communication.Classification is null ? null : Snapshot(communication.Classification);
        var now = DateTimeOffset.UtcNow;

        var classification = communication.Classification;

        if (classification is null)
        {
            classification = new Classification
            {
                CommunicationId = communication.Id,
                ClassifiedBy = actingUserId,
                ClassifiedAt = now,
                FormVersion = request.FormVersion ?? await CurrentVersionAsync(communication.Direction, ct),
            };

            db.Classifications.Add(classification);
        }
        else
        {
            classification.UpdatedBy = actingUserId;
            classification.UpdatedAt = now;
        }

        classification.TypeId = type.Id;
        classification.OrderValue = request.OrderValue;
        classification.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        classification.FollowUp = request.FollowUp;
        classification.CustomValues = request.CustomValues ?? JsonDocument.Parse("{}");

        // Resolved is only meaningful on a complaint (R-17). Recording it on an
        // order would put rows into the complaint-resolution report that were
        // never complaints.
        if (type.Name.Equals("Complaint", StringComparison.OrdinalIgnoreCase))
        {
            if (request.Resolved != classification.Resolved)
            {
                classification.Resolved = request.Resolved;
                classification.ResolvedAt = request.Resolved == true ? now : null;
                classification.ResolvedBy = request.Resolved == true ? actingUserId : null;
            }
        }
        else
        {
            classification.Resolved = null;
            classification.ResolvedAt = null;
            classification.ResolvedBy = null;
        }

        // The branch belongs to the call, not the classification: the reports
        // group calls by branch, and a call with no classification still has one.
        if (request.BranchId is not null)
        {
            communication.BranchId = request.BranchId;
        }

        communication.UpdatedAt = now;

        db.ClassificationHistory.Add(new ClassificationHistory
        {
            CommunicationId = communication.Id,
            ChangedBy = actingUserId,
            ChangedAt = now,
            Before = before,
            After = Snapshot(classification),
        });

        await db.SaveChangesAsync(ct);

        // Read back through the same door. Whoever got this far passed the edit
        // check, which is stricter than the read one, so it always answers.
        var (saved, _) = await GetAsync(communication.Id, actingUserId, actorIsSupervisor, ct);
        return (saved, null);
    }

    /// <summary>
    /// Classifies a call the server has not been told about yet (A-04, A-40).
    /// </summary>
    /// <remarks>
    /// The Agent App queues calls locally while the server is unreachable, so at
    /// hang-up the agent classifies a call that has no server id. Keying on the
    /// SIP Call-ID and the extension lets the classification be queued beside
    /// the call and land correctly once both arrive.
    ///
    /// The call is looked up rather than created: the queue sends the call
    /// first, so by the time this arrives the call exists. If it does not, the
    /// caller is told, and the Agent App keeps the classification queued and
    /// tries again rather than losing what the agent typed.
    /// </remarks>
    public async Task<(ClassificationDto? Saved, Failure? Failure)> SaveByCallAsync(
        SaveClassificationByCallRequest request,
        Guid actingUserId,
        bool actorIsSupervisor,
        CancellationToken ct = default)
    {
        var communicationId = await db.Communications
            .Where(c => c.SipCallId == request.SipCallId && c.Extension == request.Extension)
            .OrderByDescending(c => c.StartedAt)
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (communicationId is null)
        {
            logger.LogInformation(
                "A classification arrived for call {SipCallId} on {Extension}, which is not logged yet",
                request.SipCallId, request.Extension);

            return (null, Failure.CommunicationNotFound);
        }

        return await SaveAsync(
            communicationId.Value, request.Classification, actingUserId, actorIsSupervisor, ct);
    }

    /// <summary>
    /// A call's classification, or null when it has none (A-42, S-03).
    /// </summary>
    /// <remarks>
    /// <b>A supervisor reads any call's; an agent only their own (A-52).</b>
    /// It used to answer any signed-in account for any call, so one agent could
    /// read what another's customer ordered or complained about. The Agent App
    /// only ever asks about the agent's own calls, from their own log.
    /// </remarks>
    public async Task<(ClassificationDto? Found, Failure? Failure)> GetAsync(
        Guid communicationId, Guid actingUserId, bool actorIsSupervisor,
        CancellationToken ct = default)
    {
        var row = await db.Classifications
            .AsNoTracking()
            .Include(c => c.Type)
            .Include(c => c.Communication).ThenInclude(c => c.Branch)
            .Include(c => c.ClassifiedByUser)
            .FirstOrDefaultAsync(c => c.CommunicationId == communicationId, ct);

        if (row is null)
        {
            return (null, null);
        }

        if (!actorIsSupervisor && row.Communication.AgentId != actingUserId)
        {
            return (null, Failure.NotYours);
        }

        var updatedBy = row.UpdatedBy is null
            ? null
            : await db.Users.Where(u => u.Id == row.UpdatedBy)
                .Select(u => u.DisplayName).FirstOrDefaultAsync(ct);

        var canEdit = await MayEditAsync(row.Communication, actingUserId, actorIsSupervisor, ct) is null;

        return (new ClassificationDto(
            row.CommunicationId,
            row.TypeId,
            row.Type.Name,
            row.Type.LabelAr,
            row.Type.LabelEn,
            row.Communication.BranchId,
            row.Communication.Branch?.Name,
            row.OrderValue,
            row.Notes,
            row.FollowUp,
            row.Resolved,
            row.FormVersion,
            row.CustomValues,
            row.ClassifiedByUser.DisplayName,
            row.ClassifiedAt,
            updatedBy,
            row.UpdatedAt,
            canEdit), null);
    }

    /// <summary>
    /// The audit trail for one call (A-43). The same door as
    /// <see cref="GetAsync"/>: a supervisor any call, an agent their own, since
    /// the trail carries everything the classification said at every step.
    /// </summary>
    public async Task<(IReadOnlyList<ClassificationHistoryDto> Changes, Failure? Failure)> HistoryAsync(
        Guid communicationId, Guid actingUserId, bool actorIsSupervisor, CancellationToken ct = default)
    {
        if (!actorIsSupervisor)
        {
            var call = await db.Communications
                .AsNoTracking()
                .Where(c => c.Id == communicationId)
                .Select(c => new { c.AgentId })
                .FirstOrDefaultAsync(ct);

            if (call is not null && call.AgentId != actingUserId)
            {
                return ([], Failure.NotYours);
            }
        }

        var changes = await db.ClassificationHistory
            .AsNoTracking()
            .Where(h => h.CommunicationId == communicationId)
            .OrderByDescending(h => h.ChangedAt)
            .Select(h => new ClassificationHistoryDto(
                h.ChangedAt, h.ChangedByUser.DisplayName, h.Before, h.After))
            .ToListAsync(ct);

        return (changes, null);
    }

    /// <summary>
    /// Whether this user may write this classification, and why not (A-42).
    /// The rule itself is <see cref="CallEditWindow"/>, shared with call notes.
    /// </summary>
    private async Task<Failure?> MayEditAsync(
        Communication communication, Guid actingUserId, bool actorIsSupervisor,
        CancellationToken ct) =>
        await editWindow.CheckAsync(communication, actingUserId, actorIsSupervisor, ct) switch
        {
            null => null,
            CallEditWindow.Refusal.NotYours => Failure.NotYours,
            _ => Failure.EditWindowClosed,
        };

    /// <summary>
    /// The current version for the call's direction, when the client did not
    /// say which form it drew.
    /// </summary>
    private async Task<int> CurrentVersionAsync(string? direction, CancellationToken ct)
    {
        var wanted = NormaliseDirection(direction);

        return await db.FormDefinitions.Where(f => f.IsCurrent && f.Direction == wanted)
            .Select(f => f.Version).FirstOrDefaultAsync(ct) is var v && v > 0 ? v : 1;
    }

    /// <summary>
    /// What a classification looked like, for the audit trail (A-43).
    /// </summary>
    /// <remarks>
    /// Stored as a snapshot rather than a list of changed fields. A field-level
    /// diff is smaller and unreadable a year later; the question the supervisor
    /// actually asks is "what did it say before", and this answers it directly.
    /// </remarks>
    private static JsonDocument Snapshot(Classification c) => JsonSerializer.SerializeToDocument(new
    {
        typeId = c.TypeId,
        orderValue = c.OrderValue,
        notes = c.Notes,
        followUp = c.FollowUp,
        resolved = c.Resolved,
        formVersion = c.FormVersion,
        customValues = c.CustomValues,
    });
}
