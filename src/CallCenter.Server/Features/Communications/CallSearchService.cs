using System.Runtime.CompilerServices;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Reports;
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
    /// <param name="ChannelIds">Which apps, for messages (S-02's channel filter).</param>
    /// <param name="Internal">
    /// <c>false</c> for the customers' calls only, the ones the reports count;
    /// <c>true</c> for the internal ones only, which the reports leave out
    /// (S-48): the Calls page's two tabs (Dia, 9 Oct 2026). Null for both.
    /// </param>
    /// <remarks>
    /// The lists match any of their values; empty or null is no filter. A
    /// supervisor can pick several agents, branches, types and so on at once
    /// (Dia, 2 Oct 2026).
    /// </remarks>
    public record Filter(
        string? Query = null,
        string? Kind = CommunicationKinds.Call,
        IReadOnlyList<Guid>? ChannelIds = null,
        IReadOnlyList<Guid>? AgentIds = null,
        IReadOnlyList<Guid>? BranchIds = null,
        IReadOnlyList<Guid>? TypeIds = null,
        IReadOnlyList<string>? Statuses = null,
        IReadOnlyList<string>? Directions = null,
        DateTimeOffset? From = null,
        DateTimeOffset? To = null,
        string? Notes = null,
        decimal? MinOrderValue = null,
        decimal? MaxOrderValue = null,
        bool? HasRecording = null,
        bool? Classified = null,
        bool? Internal = null);

    /// <summary>One page of the calls matching <paramref name="filter"/>, newest first.</summary>
    public async Task<CallSearchPageDto> SearchAsync(
        Filter filter, int page = 1, int pageSize = DefaultPageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var calls = Apply(db.Communications.AsNoTracking(), filter, await InternalNumbersAsync(filter, ct));

        var total = await calls.CountAsync(ct);

        var rows = await Project(calls
                .OrderByDescending(c => c.StartedAt)
                .ThenBy(c => c.Id)
                .Skip((page - 1) * pageSize)
                .Take(pageSize))
            .ToListAsync(ct);

        return new CallSearchPageDto(rows, total, page, pageSize);
    }

    /// <summary>
    /// Every call matching <paramref name="filter"/>, newest first, unpaged: R-02,
    /// "all calls with all details", exported in full (S-05). Streamed, so a
    /// year of calls is never held in memory at once.
    /// </summary>
    public async IAsyncEnumerable<CallSearchRowDto> ExportAsync(
        Filter filter, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var rows = Project(Apply(db.Communications.AsNoTracking(), filter, await InternalNumbersAsync(filter, ct))
                .OrderByDescending(c => c.StartedAt)
                .ThenBy(c => c.Id))
            .AsAsyncEnumerable()
            .WithCancellation(ct);

        await foreach (var row in rows) yield return row;
    }

    /// <summary>S-48's list, read only when a tab asks for it.</summary>
    private async Task<IReadOnlyList<string>> InternalNumbersAsync(Filter filter, CancellationToken ct) =>
        filter.Internal is null ? [] : await ReportScope.InternalNumbersAsync(db, ct);

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

    /// <summary>The values given, less the blank ones.</summary>
    private static string[] Words(IReadOnlyList<string>? values) =>
        values is null ? [] : values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v.Trim()).ToArray();

    private static IQueryable<Communication> Apply(
        IQueryable<Communication> calls, Filter f, IReadOnlyList<string> internalNumbers)
    {
        // Calls-only screens must not start returning messages, and the
        // Applications page must not show calls (A-70).
        if (!string.IsNullOrWhiteSpace(f.Kind)) calls = calls.Where(c => c.Kind == f.Kind);
        if (f.ChannelIds is { Count: > 0 })
        {
            var channels = f.ChannelIds.ToArray();
            calls = calls.Where(c => channels.Contains(c.ChannelId));
        }

        if (f.AgentIds is { Count: > 0 })
        {
            var agents = f.AgentIds.ToArray();
            calls = calls.Where(c => c.AgentId != null && agents.Contains(c.AgentId.Value));
        }

        if (f.BranchIds is { Count: > 0 })
        {
            var branches = f.BranchIds.ToArray();
            calls = calls.Where(c => c.BranchId != null && branches.Contains(c.BranchId.Value));
        }

        if (f.TypeIds is { Count: > 0 })
        {
            var types = f.TypeIds.ToArray();
            calls = calls.Where(c => c.Classification != null && types.Contains(c.Classification.TypeId));
        }

        // The reports' own rule, so the Customer calls tab and a report over the
        // same filters give the same number (Dia, 9 Oct 2026).
        if (f.Internal is { } only)
        {
            calls = only
                ? ReportScope.OnlyInternal(calls, internalNumbers)
                : ReportScope.WithoutInternal(calls, internalNumbers);
        }

        if (Words(f.Statuses) is { Length: > 0 } statuses) calls = calls.Where(c => statuses.Contains(c.Status));
        if (Words(f.Directions) is { Length: > 0 } directions) calls = calls.Where(c => directions.Contains(c.Direction));

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

    /// <summary>The row, built in the query: one round trip for a page, however many joins. The complaints report lists its rows the same way.</summary>
    public static IQueryable<CallSearchRowDto> Project(IQueryable<Communication> calls) =>
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
