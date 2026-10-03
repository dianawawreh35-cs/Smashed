using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Contacts;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Mistakes;
using CallCenter.Shared.Phone;
using CallCenter.Shared.Text;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Mistakes;

/// <summary>
/// The mistakes made by the branches and the agents, as the supervisor records
/// them (S-65): record, search, correct, remove, export.
/// </summary>
/// <remarks>
/// <b>Filtered and paged here, never in the browser</b>, as the call search is
/// (20 Sep, "filtering a page lies"); the count and the total value are over
/// every match, not the page on screen.
///
/// <b>The customer is found the way the pop-up finds a caller</b> (A-13): the
/// exact normalised number, else the last nine digits, never the last nine of a
/// short number. A number nobody has on file is kept as typed (Dia, 1 Oct).
///
/// <b>A disabled branch or agent is not offered for a new mistake</b>, the rule
/// the classification form has for a branch, but a mistake already recorded
/// against one keeps it when it is corrected for something else.
///
/// <b>Correcting and removing are allowed</b> (Dia, 1 Oct), and each is written
/// to the audit log with what the row said before (N-06), so a removed mistake
/// can still be traced.
/// </remarks>
public class MistakesService(CallCenterDbContext db, TimeProvider clock, ILogger<MistakesService> logger)
{
    public const int MaxPageSize = 100;
    public const int DefaultPageSize = 50;

    /// <summary><c>audit_log.entity</c> for these rows.</summary>
    public const string AuditEntity = "mistake";

    public enum Failure
    {
        NotFound,
        UnknownBranch,
        BranchInactive,
        BadResponsible,
        AgentRequired,
        AgentNotAllowed,
        UnknownAgent,
        AgentInactive,
        BadNumber,
        NotesRequired,
        FutureDate,
    }

    /// <summary>What the supervisor searched for. Every field is optional.</summary>
    /// <param name="From">First day wanted, inclusive.</param>
    /// <param name="To">Last day wanted, inclusive.</param>
    /// <param name="Query">A customer's number (three or more digits) or name, or words in the notes.</param>
    /// <param name="Compensated">Only the compensated (true) or only those not (false); null is both (Dia, 3 Oct 2026).</param>
    /// <remarks>The lists match any of their values; empty or null is no filter (Dia, 2 Oct 2026).</remarks>
    public record Filter(
        DateOnly? From = null,
        DateOnly? To = null,
        IReadOnlyList<Guid>? BranchIds = null,
        IReadOnlyList<string>? Responsible = null,
        IReadOnlyList<Guid>? AgentIds = null,
        string? Query = null,
        bool? Compensated = null);

    public async Task<MistakePageDto> SearchAsync(
        Filter filter, int page = 1, int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var mistakes = Apply(db.Mistakes.AsNoTracking(), filter);

        var total = await mistakes.CountAsync(ct);
        var value = await mistakes.SumAsync(m => m.Value, ct) ?? 0m;

        var rows = await Project(Order(mistakes).Skip((page - 1) * pageSize).Take(pageSize)).ToListAsync(ct);

        return new MistakePageDto(rows, total, value, page, pageSize);
    }

    /// <summary>Every match, unpaged and streamed, for the export (S-05).</summary>
    public IAsyncEnumerable<MistakeDto> ExportAsync(Filter filter) =>
        Project(Order(Apply(db.Mistakes.AsNoTracking(), filter))).AsAsyncEnumerable();

    public async Task<(MistakeDto? Mistake, Failure? Failure)> CreateAsync(
        UpsertMistakeRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var mistake = new Mistake { CreatedBy = actingUserId, CreatedAt = now };

        if (await ApplyAsync(mistake, request, isNew: true, ct) is { } failure)
        {
            return (null, failure);
        }

        mistake.UpdatedBy = actingUserId;
        mistake.UpdatedAt = now;
        db.Mistakes.Add(mistake);
        await db.SaveChangesAsync(ct);

        await AuditAsync(actingUserId, "create", mistake.Id, before: null, after: Snapshot(mistake), ct);
        logger.LogInformation("Mistake {Id} recorded against {Responsible} by {UserId}",
            mistake.Id, mistake.Responsible, actingUserId);

        return (await GetAsync(mistake.Id, ct), null);
    }

    public async Task<(MistakeDto? Mistake, Failure? Failure)> UpdateAsync(
        Guid id, UpsertMistakeRequest request, Guid actingUserId, CancellationToken ct = default)
    {
        var mistake = await db.Mistakes.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mistake is null)
        {
            return (null, Failure.NotFound);
        }

        var before = Snapshot(mistake);

        if (await ApplyAsync(mistake, request, isNew: false, ct) is { } failure)
        {
            return (null, failure);
        }

        mistake.UpdatedBy = actingUserId;
        mistake.UpdatedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(ct);

        await AuditAsync(actingUserId, "update", id, before, Snapshot(mistake), ct);
        logger.LogInformation("Mistake {Id} corrected by {UserId}", id, actingUserId);

        return (await GetAsync(id, ct), null);
    }

    /// <summary>Removes a mistake entered by error. What it said stays in the audit log.</summary>
    public async Task<Failure?> DeleteAsync(Guid id, Guid actingUserId, CancellationToken ct = default)
    {
        var mistake = await db.Mistakes.FirstOrDefaultAsync(m => m.Id == id, ct);
        if (mistake is null)
        {
            return Failure.NotFound;
        }

        var before = Snapshot(mistake);
        db.Mistakes.Remove(mistake);
        await db.SaveChangesAsync(ct);

        await AuditAsync(actingUserId, "delete", id, before, after: null, ct);
        logger.LogInformation("Mistake {Id} removed by {UserId}", id, actingUserId);

        return null;
    }

    public Task<MistakeDto?> GetAsync(Guid id, CancellationToken ct = default) =>
        Project(db.Mistakes.AsNoTracking().Where(m => m.Id == id)).FirstOrDefaultAsync(ct);

    /// <summary>Checks the request and copies it onto <paramref name="mistake"/>, or says why not.</summary>
    private async Task<Failure?> ApplyAsync(Mistake mistake, UpsertMistakeRequest request, bool isNew, CancellationToken ct)
    {
        var notes = request.Notes?.Trim() ?? "";
        if (notes.Length == 0)
        {
            return Failure.NotesRequired;
        }

        if (request.OccurredOn > Today())
        {
            return Failure.FutureDate;
        }

        var branch = await db.Branches.AsNoTracking()
            .Where(b => b.Id == request.BranchId)
            .Select(b => new { b.IsActive })
            .FirstOrDefaultAsync(ct);

        if (branch is null)
        {
            return Failure.UnknownBranch;
        }

        // A disabled branch for a new mistake, or moved onto one, is refused;
        // an old mistake keeps its own.
        if (!branch.IsActive && (isNew || mistake.BranchId != request.BranchId))
        {
            return Failure.BranchInactive;
        }

        var responsible = MistakeResponsibilities.All.FirstOrDefault(r =>
            string.Equals(r, request.Responsible?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (responsible is null)
        {
            return Failure.BadResponsible;
        }

        // S-65: the branch's mistake names no agent; an agent's always names one.
        if (responsible == MistakeResponsibilities.Branch && request.AgentId is not null)
        {
            return Failure.AgentNotAllowed;
        }

        if (responsible == MistakeResponsibilities.Agent)
        {
            if (request.AgentId is not { } agentId)
            {
                return Failure.AgentRequired;
            }

            var agent = await db.Users.AsNoTracking()
                .Where(u => u.Id == agentId && u.Role == UserRoles.Agent)
                .Select(u => new { u.IsActive })
                .FirstOrDefaultAsync(ct);

            if (agent is null)
            {
                return Failure.UnknownAgent;
            }

            if (!agent.IsActive && (isNew || mistake.AgentId != agentId))
            {
                return Failure.AgentInactive;
            }
        }

        string? raw = string.IsNullOrWhiteSpace(request.CustomerNumber) ? null : request.CustomerNumber.Trim();
        string? normalised = null;
        Guid? contactId = null;

        if (raw is not null)
        {
            normalised = PhoneNormalizer.Normalize(raw);
            if (string.IsNullOrEmpty(normalised))
            {
                return Failure.BadNumber;
            }

            // The same number as before keeps the customer it was linked to;
            // a new one is looked up again.
            contactId = !isNew && mistake.CustomerNormalised == normalised && mistake.ContactId is not null
                ? mistake.ContactId
                : await FindContactAsync(raw, normalised, ct);
        }

        mistake.OccurredOn = request.OccurredOn;
        mistake.BranchId = request.BranchId;
        mistake.Responsible = responsible;
        mistake.AgentId = responsible == MistakeResponsibilities.Agent ? request.AgentId : null;
        mistake.Value = request.Value;
        mistake.Compensated = request.Compensated;
        mistake.CustomerNumberRaw = raw;
        mistake.CustomerNormalised = normalised;
        mistake.ContactId = contactId;
        mistake.Notes = notes;

        return null;
    }

    /// <summary>The caller lookup's rule (A-13): exact, else the last nine digits of a full number.</summary>
    private async Task<Guid?> FindContactAsync(string raw, string normalised, CancellationToken ct)
    {
        var active = db.Contacts.AsNoTracking().Where(c => c.DeletedAt == null && c.MergedIntoId == null);

        var exact = await active
            .Where(c => c.Phones.Any(p => p.Normalised == normalised))
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(ct);

        if (exact is not null)
        {
            return exact;
        }

        var last9 = PhoneNormalizer.Last9(raw);
        if (last9.Length != 9)
        {
            return null;
        }

        return await active.ByLast9(last9).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(ct);
    }

    /// <summary>The restaurant's today: the server runs in its time zone, as the reports assume.</summary>
    private DateOnly Today() =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), TimeZoneInfo.Local).DateTime);

    /// <summary>The filters, as SQL. The reports (R-23) narrow by the same rules as the list.</summary>
    internal static IQueryable<Mistake> Apply(IQueryable<Mistake> mistakes, Filter f)
    {
        if (f.From is { } from) mistakes = mistakes.Where(m => m.OccurredOn >= from);
        if (f.To is { } to) mistakes = mistakes.Where(m => m.OccurredOn <= to);
        if (f.BranchIds is { Count: > 0 })
        {
            var branches = f.BranchIds.ToArray();
            mistakes = mistakes.Where(m => branches.Contains(m.BranchId));
        }

        if (f.Compensated is { } compensated) mistakes = mistakes.Where(m => m.Compensated == compensated);

        if (f.AgentIds is { Count: > 0 })
        {
            var agents = f.AgentIds.ToArray();
            mistakes = mistakes.Where(m => m.AgentId != null && agents.Contains(m.AgentId.Value));
        }

        var responsible = (f.Responsible ?? [])
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .ToArray();
        if (responsible.Length > 0)
        {
            mistakes = mistakes.Where(m => responsible.Contains(m.Responsible));
        }

        if (!string.IsNullOrWhiteSpace(f.Query))
        {
            var text = f.Query.Trim();
            var digits = PhoneNormalizer.DigitsOnly(text);

            // A number is matched as the call search matches one: a whole number
            // on its last nine digits, a shorter piece anywhere, less a leading 0.
            if (digits.Length >= 3 && text.All(ch => char.IsDigit(ch) || " +-()".Contains(ch)))
            {
                var piece = digits.Length >= 9 ? digits[^9..]
                    : digits.StartsWith('0') ? digits[1..]
                    : digits;
                var pieceLike = $"%{piece}%";

                mistakes = mistakes.Where(m => m.CustomerNormalised != null && EF.Functions.ILike(m.CustomerNormalised, pieceLike));
            }
            else
            {
                var nameLike = $"%{NameNormalizer.Normalize(text)}%";
                var like = $"%{text}%";

                mistakes = mistakes.Where(m =>
                    EF.Functions.ILike(m.Notes, like)
                    || (m.Contact != null && m.Contact.NameNormalised != null
                        && EF.Functions.ILike(m.Contact.NameNormalised, nameLike)));
            }
        }

        return mistakes;
    }

    private static IQueryable<Mistake> Order(IQueryable<Mistake> mistakes) =>
        mistakes.OrderByDescending(m => m.OccurredOn).ThenByDescending(m => m.CreatedAt).ThenBy(m => m.Id);

    private static IQueryable<MistakeDto> Project(IQueryable<Mistake> mistakes) =>
        mistakes.Select(m => new MistakeDto(
            m.Id,
            m.OccurredOn,
            m.BranchId,
            m.Branch.Name,
            m.Responsible,
            m.AgentId,
            m.Agent != null ? m.Agent.DisplayName : null,
            m.Value,
            m.Compensated,
            m.ContactId,
            m.Contact != null ? m.Contact.Name : null,
            m.CustomerNumberRaw,
            m.Notes,
            m.Creator != null ? m.Creator.DisplayName : null,
            m.CreatedAt,
            m.UpdatedAt));

    private static JsonDocument Snapshot(Mistake m) => JsonSerializer.SerializeToDocument(new
    {
        OccurredOn = m.OccurredOn.ToString("yyyy-MM-dd"),
        m.BranchId,
        m.Responsible,
        m.AgentId,
        m.Value,
        m.Compensated,
        m.CustomerNumberRaw,
        m.ContactId,
        m.Notes,
    });

    private async Task AuditAsync(
        Guid actingUserId, string action, Guid id, JsonDocument? before, JsonDocument? after, CancellationToken ct)
    {
        db.AuditLog.Add(new AuditLogEntry
        {
            UserId = actingUserId,
            At = clock.GetUtcNow(),
            Entity = AuditEntity,
            EntityId = id.ToString(),
            Action = action,
            Before = before,
            After = after,
        });

        await db.SaveChangesAsync(ct);
    }
}
