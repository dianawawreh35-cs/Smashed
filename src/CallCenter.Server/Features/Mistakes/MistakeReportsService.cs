using CallCenter.Server.Data;
using CallCenter.Server.Features.Reports;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Mistakes;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Mistakes;

/// <summary>
/// The mistakes report (R-23): per branch, per agent, over time, and the
/// customers it happened to more than once.
/// </summary>
/// <remarks>
/// <b>Narrowed by the list's own rules</b> (<see cref="MistakesService.Apply"/>):
/// the days, the branch, the agent and compensated or not, so the report and
/// the Mistakes page count the same rows for the same filters.
///
/// <b>Every card says how many were compensated</b> (تم التعويض; Dia, 3 Oct
/// 2026), and the value of those, beside the count and value of them all.
///
/// <b>Summed here, after one query</b>, rather than grouped in SQL. Mistakes are
/// typed in by a supervisor, a few a day at most, so a year is a few thousand
/// small rows; reading them and adding them up is simpler than four GROUP BYs,
/// and still on the server, never a page in the browser.
/// </remarks>
public class MistakeReportsService(CallCenterDbContext db)
{
    /// <summary>What each report reads of a mistake.</summary>
    private sealed record Row(
        DateOnly OccurredOn,
        Guid BranchId,
        string Branch,
        string Responsible,
        Guid? AgentId,
        string? Agent,
        decimal? Value,
        bool Compensated,
        Guid? ContactId,
        string? Customer,
        string? Normalised,
        string? Number,
        DateTimeOffset CreatedAt);

    public async Task<IReadOnlyList<MistakeBranchRowDto>> ByBranchAsync(MistakesService.Filter filter, CancellationToken ct = default)
    {
        var rows = await RowsAsync(filter, ct);

        return rows
            .GroupBy(r => r.BranchId)
            .Select(g => new MistakeBranchRowDto(
                g.Key,
                g.First().Branch,
                g.Count(),
                g.Count(r => r.Responsible == MistakeResponsibilities.Branch),
                g.Count(r => r.Responsible == MistakeResponsibilities.Agent),
                g.Sum(r => r.Value ?? 0),
                g.Count(r => r.Compensated),
                CompensatedValue(g)))
            .OrderByDescending(r => r.Mistakes).ThenByDescending(r => r.Value).ThenBy(r => r.Branch)
            .ToList();
    }

    /// <summary>Only the mistakes put down to an agent; a branch's own names nobody.</summary>
    public async Task<IReadOnlyList<MistakeAgentRowDto>> ByAgentAsync(MistakesService.Filter filter, CancellationToken ct = default)
    {
        var rows = await RowsAsync(filter, ct);

        return rows
            .Where(r => r.AgentId is not null)
            .GroupBy(r => r.AgentId!.Value)
            .Select(g => new MistakeAgentRowDto(
                g.Key, g.First().Agent ?? "", g.Count(), g.Sum(r => r.Value ?? 0), g.Count(r => r.Compensated), CompensatedValue(g)))
            .OrderByDescending(r => r.Mistakes).ThenByDescending(r => r.Value).ThenBy(r => r.Agent)
            .ToList();
    }

    /// <param name="groupBy"><c>day</c> (the default), <c>week</c> or <c>month</c>, as the other reports' trends.</param>
    public async Task<IReadOnlyList<MistakeTrendPointDto>> TrendAsync(
        MistakesService.Filter filter, string? groupBy, CancellationToken ct = default)
    {
        var rows = await RowsAsync(filter, ct);

        return rows
            .GroupBy(r => ReportScope.Bucket(r.OccurredOn.ToDateTime(TimeOnly.MinValue), groupBy))
            .Select(g => new MistakeTrendPointDto(
                g.Key,
                g.Count(),
                g.Count(r => r.Responsible == MistakeResponsibilities.Branch),
                g.Count(r => r.Responsible == MistakeResponsibilities.Agent),
                g.Sum(r => r.Value ?? 0),
                g.Count(r => r.Compensated),
                CompensatedValue(g)))
            .OrderBy(p => p.Bucket)
            .ToList();
    }

    /// <summary>
    /// Customers with two or more mistakes in the period, most first. A saved
    /// customer is one row whichever of their numbers was typed; a number
    /// nobody has on file is a customer of its own.
    /// </summary>
    public async Task<IReadOnlyList<MistakeCustomerRowDto>> RepeatCustomersAsync(
        MistakesService.Filter filter, CancellationToken ct = default)
    {
        var rows = await RowsAsync(filter, ct);

        return rows
            .Where(r => r.Normalised is not null)
            .GroupBy(r => r.ContactId?.ToString() ?? "number:" + r.Normalised)
            .Where(g => g.Count() >= 2)
            .Select(g =>
            {
                var latest = g.OrderByDescending(r => r.OccurredOn).ThenByDescending(r => r.CreatedAt).First();
                return new MistakeCustomerRowDto(
                    latest.ContactId, latest.Customer, latest.Number ?? "", g.Count(), g.Sum(r => r.Value ?? 0),
                    g.Count(r => r.Compensated), latest.OccurredOn);
            })
            .OrderByDescending(r => r.Mistakes).ThenByDescending(r => r.Value).ThenByDescending(r => r.Last)
            .ToList();
    }

    private static decimal CompensatedValue(IEnumerable<Row> rows) => rows.Where(r => r.Compensated).Sum(r => r.Value ?? 0);

    private async Task<List<Row>> RowsAsync(MistakesService.Filter filter, CancellationToken ct) =>
        await MistakesService.Apply(db.Mistakes.AsNoTracking(), filter)
            .Select(m => new Row(
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
                m.CustomerNormalised,
                m.CustomerNumberRaw,
                m.CreatedAt))
            .ToListAsync(ct);
}
