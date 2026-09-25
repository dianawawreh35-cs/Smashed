using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Phone;
using CallCenter.Shared.Text;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Communications;

/// <summary>
/// The supervisor's search across every call, and one call opened in full
/// (S-02, S-03).
/// </summary>
/// <remarks>
/// <b>Filtered and paged here, never in the browser.</b> Filtering a fetched
/// page answers "no match" whenever the match is older than the page, which
/// reads as "this never happened" (20 September). So every filter is part of the
/// query, and the total is counted over all of them.
///
/// Every filter narrows an indexed column or a join on a primary key, apart from
/// the notes text, which is an <c>ILIKE</c> over two note columns. At the
/// expected volume (about 500 calls a day, N-02) a year is under 200,000 rows.
/// That scan is well inside the 5-second budget, and it only runs when somebody
/// types in the notes box.
/// </remarks>
public class CallSearchService(CallCenterDbContext db)
{
    /// <summary>The largest page a request can ask for.</summary>
    public const int MaxPageSize = 100;

    /// <summary>A page when the request does not say.</summary>
    public const int DefaultPageSize = 50;

    /// <summary>What the supervisor asked for. Every field is optional.</summary>
    /// <param name="Query">A number (three or more digits), or part of a contact's name or the caller-ID name.</param>
    /// <param name="From">Inclusive instant. The browser sends the start of the day it means, in its own time zone.</param>
    /// <param name="To">Exclusive instant: the start of the day after the last one wanted.</param>
    /// <param name="Notes">Text in the classification's notes or the call's own note.</param>
    /// <param name="Kind">
    /// <c>Call</c> or <c>App</c>. The Calls page asks for calls and the
    /// Applications page for messages (A-70); neither may show the other's
    /// rows, so this is never blank in practice. Null means both, for a
    /// combined view later.
    /// </param>
    /// <param name="ChannelId">Which app, for messages (S-02's channel filter).</param>
    public record Filter(
        string? Query = null,
        string? Kind = CommunicationKinds.Call,
        Guid? ChannelId = null,
        Guid? AgentId = null,
        Guid? BranchId = null,
        Guid? TypeId = null,
        string? Status = null,
        string? Direction = null,
        DateTimeOffset? From = null,
        DateTimeOffset? To = null,
        string? Notes = null,
        decimal? MinOrderValue = null,
        decimal? MaxOrderValue = null,
        bool? HasRecording = null,
        bool? Classified = null);

    /// <summary>One page of the calls matching <paramref name="filter"/>, newest first.</summary>
    public async Task<CallSearchPageDto> SearchAsync(
        Filter filter, int page = 1, int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var calls = Apply(db.Communications.AsNoTracking(), filter);

        var total = await calls.CountAsync(ct);

        var rows = await Project(calls
                .OrderByDescending(c => c.StartedAt)
                .ThenBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(ct);

        return new CallSearchPageDto(rows, total, page, pageSize);
    }

    /// <summary>One call in full, or null when there is no such call.</summary>
    public async Task<CallDetailsDto?> DetailsAsync(Guid id, CancellationToken ct = default)
    {
        var call = db.Communications.AsNoTracking().Where(c => c.Id == id);

        var summary = await Project(call).FirstOrDefaultAsync(ct);
        if (summary is null)
        {
            return null;
        }

        var extra = await call
            .Select(c => new { c.RemoteName, c.AnsweredAt, c.EndedAt, c.WaitSec, c.QueueName, c.Extension, c.Notes })
            .FirstAsync(ct);

        return new CallDetailsDto(
            summary,
            extra.RemoteName,
            extra.AnsweredAt,
            extra.EndedAt,
            extra.WaitSec,
            extra.QueueName,
            extra.Extension,
            extra.Notes);
    }

    private static IQueryable<Communication> Apply(IQueryable<Communication> calls, Filter f)
    {
        // Calls-only screens must not start returning messages, and the
        // Applications page must not show calls (A-70).
        if (!string.IsNullOrWhiteSpace(f.Kind)) calls = calls.Where(c => c.Kind == f.Kind);
        if (f.ChannelId is { } channel) calls = calls.Where(c => c.ChannelId == channel);
        if (f.AgentId is { } agent) calls = calls.Where(c => c.AgentId == agent);
        if (f.BranchId is { } branch) calls = calls.Where(c => c.BranchId == branch);
        if (f.TypeId is { } type) calls = calls.Where(c => c.Classification != null && c.Classification.TypeId == type);
        if (!string.IsNullOrWhiteSpace(f.Status)) calls = calls.Where(c => c.Status == f.Status);
        if (!string.IsNullOrWhiteSpace(f.Direction)) calls = calls.Where(c => c.Direction == f.Direction);

        // Instants, converted to UTC: PostgreSQL's timestamptz takes nothing
        // else from Npgsql, and a local offset here was the 20 September 500.
        if (f.From is { } from)
        {
            var start = from.ToUniversalTime();
            calls = calls.Where(c => c.StartedAt >= start);
        }

        if (f.To is { } to)
        {
            var end = to.ToUniversalTime();
            calls = calls.Where(c => c.StartedAt < end);
        }

        if (f.MinOrderValue is { } min)
        {
            calls = calls.Where(c => c.Classification != null && c.Classification.OrderValue >= min);
        }

        if (f.MaxOrderValue is { } max)
        {
            calls = calls.Where(c => c.Classification != null && c.Classification.OrderValue <= max);
        }

        if (f.HasRecording is { } recorded)
        {
            calls = recorded
                ? calls.Where(c => c.Recording != null && c.Recording.DeletedAt == null)
                : calls.Where(c => c.Recording == null || c.Recording.DeletedAt != null);
        }

        if (f.Classified is { } classified)
        {
            calls = classified
                ? calls.Where(c => c.Classification != null)
                : calls.Where(c => c.Classification == null);
        }

        if (!string.IsNullOrWhiteSpace(f.Notes))
        {
            var like = $"%{f.Notes.Trim()}%";
            calls = calls.Where(c =>
                (c.Notes != null && EF.Functions.ILike(c.Notes, like))
                || (c.Classification != null && c.Classification.Notes != null
                    && EF.Functions.ILike(c.Classification.Notes, like)));
        }

        if (!string.IsNullOrWhiteSpace(f.Query))
        {
            var text = f.Query.Trim();
            var digits = PhoneNormalizer.DigitsOnly(text);

            // Digits and the punctuation people type around them is a number, as
            // in the contacts search, and "+970 599…" is a number, not a name.
            // A whole number is matched on its last nine digits, the same rule
            // as caller lookup (A-13), so 0599…, +970599… and 00970599… all find
            // the same calls. A shorter piece is looked for anywhere in it, less
            // a leading trunk zero, so "0599" finds a call stored as 970599….
            if (digits.Length >= 3 && text.All(ch => char.IsDigit(ch) || " +-()".Contains(ch)))
            {
                var piece = digits.Length >= 9 ? digits[^9..]
                    : digits.StartsWith('0') ? digits[1..]
                    : digits;
                var pieceLike = $"%{piece}%";
                var digitsLike = $"%{digits}%";

                calls = calls.Where(c =>
                    (c.RemoteNormalised != null && EF.Functions.ILike(c.RemoteNormalised, pieceLike))
                    || (c.RemoteNumberRaw != null && EF.Functions.ILike(c.RemoteNumberRaw, digitsLike)));
            }
            else
            {
                // The normalised name, so "احمد" finds a contact saved as "أحمد" (A-80).
                var nameLike = $"%{NameNormalizer.Normalize(text)}%";
                var like = $"%{text}%";

                calls = calls.Where(c =>
                    (c.Contact != null && c.Contact.NameNormalised != null
                        && EF.Functions.ILike(c.Contact.NameNormalised, nameLike))
                    || (c.RemoteName != null && EF.Functions.ILike(c.RemoteName, like)));
            }
        }

        return calls;
    }

    /// <summary>The row, built in the query: one round trip for a page, however many joins.</summary>
    private static IQueryable<CallSearchRowDto> Project(IQueryable<Communication> calls) =>
        calls.Select(c => new CallSearchRowDto(
            c.Id,
            c.Kind,
            c.StartedAt,
            c.Direction,
            c.Status,
            c.AgentId,
            c.Agent != null ? c.Agent.DisplayName : null,
            c.ContactId,
            c.Contact != null ? c.Contact.Name : null,
            c.RemoteNumberRaw,
            c.BranchId,
            c.Branch != null ? c.Branch.Name : null,
            c.Classification != null ? c.Classification.Type.Name : null,
            c.Classification != null ? c.Classification.Type.LabelAr : null,
            c.Classification != null ? c.Classification.Type.LabelEn : null,
            c.Classification != null ? c.Classification.OrderValue : null,
            c.DurationSec,
            c.Classification != null && c.Classification.Notes != null ? c.Classification.Notes : c.Notes,
            c.Classification != null,
            c.Recording != null && c.Recording.DeletedAt == null,
            c.Recording != null && c.Recording.DeletedAt != null,
            c.ChannelId,
            c.Channel.Name));
}
