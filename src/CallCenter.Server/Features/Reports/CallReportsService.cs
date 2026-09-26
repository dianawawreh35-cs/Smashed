using CallCenter.Server.Data;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Text;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Features.Reports;

/// <summary>
/// The call reports (R-01 to R-18) and the dashboard's figures (S-20).
/// </summary>
/// <remarks>
/// <b>Calls only, except where the point is the comparison</b> (Dia, 25 Sep):
/// R-01 counts calls against messages, R-12 to R-14 compare the phone with
/// the apps, and the dashboard shows communications by channel. Each method
/// says which by the kind it puts on the filter (<see cref="Calls"/>,
/// <see cref="CallsAndMessages"/>), so a caller cannot get it wrong.
///
/// <b>Counted by the database, named here.</b> The figures come from
/// <see cref="ReportCube"/>, grouped by only what each report needs (N-02:
/// a year is a few hundred cells, not 180,000 rows). The words — missed,
/// answered, unclassified — are decided once, below, from a cell's kind,
/// direction and status, and are also written in <c>CallReportsDto.cs</c>
/// where the screens can read them. The lists (complaints, cancellations,
/// missed calls) are rows, not figures, and are read through
/// <see cref="ReportScope.Narrow"/>.
/// </remarks>
public class CallReportsService(CallCenterDbContext db, ReportCube cube)
{
    /// <summary>What every status-based figure needs to know about a cell.</summary>
    private const CubeBy Outcome = CubeBy.Kind | CubeBy.Direction | CubeBy.Status;

    private static ReportFilter Calls(ReportFilter f) => f with { Kind = CommunicationKinds.Call };

    private static ReportFilter CallsAndMessages(ReportFilter f) => f with { Kind = null };

    // ---- the words, once ----------------------------------------------------------

    private static bool IsCall(CubeCell c) => c.Kind == CommunicationKinds.Call;
    private static bool IsMessage(CubeCell c) => c.Kind == CommunicationKinds.App;
    private static bool IsInbound(CubeCell c) => IsCall(c) && c.Direction == Directions.In;
    private static bool IsOutbound(CubeCell c) => IsCall(c) && c.Direction == Directions.Out;

    /// <summary>A customer rang and nobody here took it: Missed or Rejected, every row, never NoAnswer (A-21; Dia, 25 Sep).</summary>
    private static bool IsMissed(CubeCell c) =>
        IsInbound(c) && c.Status is CommunicationStatuses.Missed or CommunicationStatuses.Rejected;

    private static bool IsAnswered(CubeCell c) => IsCall(c) && c.Status == CommunicationStatuses.Answered;

    /// <summary>S-55: a customer rang and gave up in the queue before anybody took it. From the PBX, never an agent's.</summary>
    private static bool IsAbandoned(CubeCell c) => IsInbound(c) && c.Status == CommunicationStatuses.Abandoned;

    /// <summary>A-41: only an answered call is classified, so only an answered call can be unclassified.</summary>
    private static int Unclassified(CubeCell c) => IsAnswered(c) ? c.N - c.Classified : 0;

    private static int Count(IEnumerable<CubeCell> cells, Func<CubeCell, bool> which) => cells.Where(which).Sum(c => c.N);

    // ---- R-01 -----------------------------------------------------------------

    /// <summary>
    /// R-01: communications in the period — total, calls vs app, inbound vs
    /// outbound, answered vs missed — per day, week or month.
    /// </summary>
    public async Task<IReadOnlyList<CallSummaryRowDto>> SummaryAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default)
    {
        var cells = await cube.CountAsync(CallsAndMessages(filter), CubeBy.Day | Outcome, ct);
        var grouping = TimeGrouping(groupBy);

        return cells
            .GroupBy(c => ReportScope.Bucket(c.Day!.Value, grouping))
            .Select(g => new CallSummaryRowDto(
                g.Key,
                g.Sum(c => c.N),
                Count(g, IsCall),
                Count(g, IsMessage),
                Count(g, IsInbound),
                Count(g, IsOutbound),
                Count(g, c => IsInbound(c) && IsAnswered(c)),
                Count(g, IsMissed),
                Count(g, c => IsInbound(c) && c.Status == CommunicationStatuses.Blocked),
                Count(g, IsAbandoned)))
            .OrderBy(r => r.Bucket)
            .ToList();
    }

    // ---- R-03 -----------------------------------------------------------------

    /// <summary>R-03: calls per type, with each type's share of the classified calls.</summary>
    public async Task<IReadOnlyList<TypeShareRowDto>> ByTypeAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var cells = (await cube.CountAsync(Calls(filter), CubeBy.Type, ct)).Where(c => c.TypeId is not null).ToList();
        var names = await cube.NamesAsync(ct);
        var classified = cells.Sum(c => c.N);

        return cells
            .Select(c => (Type: names.Types[c.TypeId!.Value], c.N))
            .Select(x => new TypeShareRowDto(x.Type.Name, x.Type.LabelAr, x.Type.LabelEn, x.N, Percent(x.N, classified) ?? 0m))
            .OrderByDescending(r => r.Count).ThenBy(r => r.TypeName)
            .ToList();
    }

    // ---- R-04 -----------------------------------------------------------------

    /// <summary>R-04: calls per day, week, month, agent or branch, and per type within each.</summary>
    public async Task<IReadOnlyList<CallBreakdownRowDto>> BreakdownAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default)
    {
        var by = groupBy ?? "day";
        var cells = await cube.CountAsync(Calls(filter), Outcome | CubeBy.Type | Dimension(by), ct);
        var names = await cube.NamesAsync(ct);
        var heading = Heading(by, names);

        var grouped = cells
            .GroupBy(heading)
            .Select(g => new CallBreakdownRowDto(
                g.Key.Key,
                g.Key.Label,
                g.Sum(c => c.N),
                Count(g, IsAnswered),
                Count(g, IsMissed),
                g.Sum(c => c.Orders),
                g.Sum(c => c.OrderValue),
                TypeCounts(g, names)));

        return (IsTime(by) ? grouped.OrderBy(r => r.Key) : grouped.OrderByDescending(r => r.Calls).ThenBy(r => r.Label)).ToList();
    }

    /// <summary>R-04: customers who called more than once in the period, most calls first.</summary>
    public async Task<IReadOnlyList<CustomerRankRowDto>> RecurringCustomersAsync(
        ReportFilter filter, CancellationToken ct = default) =>
        (await CustomersAsync(Calls(filter), ct))
            .Where(c => c.Calls > 1)
            .OrderByDescending(c => c.Calls).ThenByDescending(c => c.OrderValue).ThenBy(c => c.Name)
            .ToList();

    // ---- R-05 and R-14's lists ---------------------------------------------------

    /// <summary>
    /// R-05's list, and R-14's: every complaint (or cancellation) in the period,
    /// newest first, with its notes and follow-up status.
    /// </summary>
    /// <param name="typeName"><c>Complaint</c> or <c>Cancellation</c>.</param>
    /// <param name="allChannels">True for R-14, which compares channels; R-05 is calls only.</param>
    public async Task<IReadOnlyList<ProblemRowDto>> ProblemsAsync(
        ReportFilter filter, string typeName, bool allChannels, CancellationToken ct = default)
    {
        var f = allChannels ? CallsAndMessages(filter) : Calls(filter);
        var q = ReportScope.Narrow(db.Communications.AsNoTracking(), f, await ReportScope.InternalNumbersAsync(db, ct))
            .Where(c => c.Classification != null && c.Classification.Type.Name == typeName);

        return await q
            .OrderByDescending(c => c.StartedAt).ThenBy(c => c.Id)
            .Select(c => new ProblemRowDto(
                c.Id,
                c.StartedAt,
                c.Channel.Name,
                c.ContactId,
                c.Contact != null ? c.Contact.Name : null,
                c.RemoteNumberRaw,
                c.Agent != null ? c.Agent.DisplayName : null,
                c.Branch != null ? c.Branch.Name : null,
                c.Classification!.Notes,
                c.Classification.FollowUp,
                c.Classification.Resolved == true,
                c.Classification.ResolvedAt))
            .ToListAsync(ct);
    }

    // ---- R-05 per branch or agent, and R-17 ----------------------------------------

    /// <summary>
    /// R-05 per branch or agent, and R-17: complaints, how many needed a
    /// follow-up, how many are resolved and how long that took, and complaints
    /// per 100 orders under the same heading.
    /// </summary>
    /// <param name="groupBy"><c>branch</c> (the default), <c>agent</c>, <c>day</c>, <c>week</c> or <c>month</c>.</param>
    public async Task<IReadOnlyList<ComplaintsRowDto>> ComplaintsByAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default)
    {
        var by = groupBy ?? "branch";
        var cells = await cube.CountAsync(Calls(filter), Dimension(by), ct);
        var names = await cube.NamesAsync(ct);

        var grouped = cells
            .GroupBy(Heading(by, names))
            .Where(g => g.Sum(c => c.Complaints) > 0)
            .Select(g =>
            {
                var complaints = g.Sum(c => c.Complaints);
                var resolved = g.Sum(c => c.Resolved);
                var timed = g.Sum(c => c.ResolvedTimed);
                var orders = g.Sum(c => c.Orders);
                return new ComplaintsRowDto(
                    g.Key.Key,
                    g.Key.Label,
                    complaints,
                    g.Sum(c => c.FollowUp),
                    resolved,
                    complaints - resolved,
                    timed == 0 ? null : Math.Round((decimal)(g.Sum(c => c.ResolveHours) / timed), 1),
                    orders,
                    orders == 0 ? null : Math.Round(complaints * 100m / orders, 1));
            });

        return (IsTime(by) ? grouped.OrderBy(r => r.Key) : grouped.OrderByDescending(r => r.Complaints).ThenBy(r => r.Label)).ToList();
    }

    /// <summary>R-05: customers who complained more than once in the period.</summary>
    public async Task<IReadOnlyList<CustomerRankRowDto>> RepeatComplainersAsync(
        ReportFilter filter, CancellationToken ct = default) =>
        (await CustomersAsync(Calls(filter), ct))
            .Where(c => c.Complaints > 1)
            .OrderByDescending(c => c.Complaints).ThenByDescending(c => c.LastAt)
            .ToList();

    // ---- R-10 -------------------------------------------------------------------------

    /// <summary>
    /// R-10: incoming calls per hour of the day and weekday, for staffing. All
    /// 24 hours and all seven days, quiet ones as zero, so the heat table is
    /// whole. Incoming only: the calls agents make are not demand.
    /// </summary>
    public async Task<IReadOnlyList<PeakHourRowDto>> PeakHoursAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var cells = (await cube.CountAsync(Calls(filter), CubeBy.Day | CubeBy.Hour | Outcome, ct)).Where(IsInbound).ToList();
        var grid = new int[24, 7];
        foreach (var c in cells)
        {
            var weekday = ((int)c.Day!.Value.DayOfWeek + 6) % 7; // Monday first, as the weeks are
            grid[c.Hour!.Value, weekday] += c.N;
        }

        return Enumerable.Range(0, 24)
            .Select(h =>
            {
                var days = Enumerable.Range(0, 7).Select(d => grid[h, d]).ToList();
                return new PeakHourRowDto(h, days, days.Sum());
            })
            .ToList();
    }

    // ---- R-11 -------------------------------------------------------------------------

    /// <summary>
    /// R-11: missed and abandoned calls, and their share of incoming calls, per
    /// day, week, month, hour, agent or branch. Missed, rejected and abandoned
    /// are shown apart and together. The time until the customer was called
    /// back is R-20's, with the abandoned calls it belongs to (S-55).
    /// </summary>
    public async Task<IReadOnlyList<MissedRowDto>> MissedAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default)
    {
        var by = groupBy ?? "day";
        var cells = (await cube.CountAsync(Calls(filter), Outcome | Dimension(by), ct)).Where(IsInbound).ToList();
        var names = await cube.NamesAsync(ct);

        var grouped = cells
            .GroupBy(Heading(by, names))
            .Select(g =>
            {
                var inbound = g.Sum(c => c.N);
                var missed = Count(g, c => c.Status == CommunicationStatuses.Missed);
                var rejected = Count(g, c => c.Status == CommunicationStatuses.Rejected);
                var abandoned = Count(g, c => c.Status == CommunicationStatuses.Abandoned);
                var total = missed + rejected + abandoned;
                return new MissedRowDto(g.Key.Key, g.Key.Label, inbound, missed, rejected, abandoned, total,
                    Percent(total, inbound));
            });

        return (IsTime(by) ? grouped.OrderBy(r => r.Key) : grouped.OrderByDescending(r => r.Total).ThenBy(r => r.Label)).ToList();
    }

    /// <summary>R-11's list: every missed and abandoned call in the period, newest first, with the note the agent left (A-41).</summary>
    public async Task<IReadOnlyList<MissedCallRowDto>> MissedListAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var q = ReportScope.Narrow(db.Communications.AsNoTracking(), Calls(filter), await ReportScope.InternalNumbersAsync(db, ct))
            .Where(c => c.Direction == Directions.In
                && (c.Status == CommunicationStatuses.Missed || c.Status == CommunicationStatuses.Rejected
                    || c.Status == CommunicationStatuses.Abandoned));

        return await q
            .OrderByDescending(c => c.StartedAt).ThenBy(c => c.Id)
            .Select(c => new MissedCallRowDto(
                c.Id,
                c.StartedAt,
                c.Status,
                c.RemoteNumberRaw,
                c.ContactId,
                c.Contact != null ? c.Contact.Name : null,
                c.Agent != null ? c.Agent.DisplayName : null,
                c.Branch != null ? c.Branch.Name : null,
                c.Notes))
            .ToListAsync(ct);
    }

    // ---- R-12 -------------------------------------------------------------------------

    /// <summary>R-12: orders on each channel, phone and apps, and each channel's share of all orders.</summary>
    public async Task<IReadOnlyList<ChannelOrdersRowDto>> OrdersByChannelAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var cells = (await cube.CountAsync(CallsAndMessages(filter), CubeBy.Channel, ct)).Where(c => c.Orders > 0).ToList();
        var names = await cube.NamesAsync(ct);
        var total = cells.Sum(c => c.Orders);

        return cells
            .Select(c => new ChannelOrdersRowDto(c.ChannelId!.Value, names.Channel(c.ChannelId), c.Orders, c.OrderValue,
                Percent(c.Orders, total) ?? 0m))
            .OrderByDescending(r => r.Orders).ThenBy(r => r.Channel)
            .ToList();
    }

    /// <summary>R-12's trend: orders by phone and through the apps, per day, week or month.</summary>
    public async Task<IReadOnlyList<ChannelOrdersTrendPointDto>> OrdersTrendAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default)
    {
        var cells = await cube.CountAsync(CallsAndMessages(filter), CubeBy.Day | CubeBy.Kind, ct);
        var grouping = TimeGrouping(groupBy);

        return cells
            .GroupBy(c => ReportScope.Bucket(c.Day!.Value, grouping))
            .Where(g => g.Sum(c => c.Orders) > 0)
            .Select(g => new ChannelOrdersTrendPointDto(
                g.Key,
                g.Where(IsCall).Sum(c => c.Orders),
                g.Where(IsMessage).Sum(c => c.Orders),
                g.Where(IsCall).Sum(c => c.OrderValue),
                g.Where(IsMessage).Sum(c => c.OrderValue)))
            .OrderBy(p => p.Bucket)
            .ToList();
    }

    // ---- R-14 -------------------------------------------------------------------------

    /// <summary>R-14: cancellations ÷ orders per branch, channel or agent, phone and apps together.</summary>
    public async Task<IReadOnlyList<CancellationRateRowDto>> CancellationsAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default)
    {
        var by = groupBy ?? "branch";
        var cells = await cube.CountAsync(CallsAndMessages(filter), Dimension(by), ct);
        var names = await cube.NamesAsync(ct);

        return cells
            .GroupBy(Heading(by, names))
            .Select(g => (g.Key, Orders: g.Sum(c => c.Orders), Cancellations: g.Sum(c => c.Cancellations)))
            .Where(x => x.Orders + x.Cancellations > 0)
            .Select(x => new CancellationRateRowDto(x.Key.Key, x.Key.Label, x.Orders, x.Cancellations, Percent(x.Cancellations, x.Orders)))
            .OrderByDescending(r => r.Cancellations).ThenBy(r => r.Label)
            .ToList();
    }

    // ---- R-15 -------------------------------------------------------------------------

    /// <summary>
    /// R-15: per agent, calls handled (answered, in and out), incoming calls
    /// answered, outgoing calls made, average talk time over answered calls,
    /// orders and their value, answered calls left unclassified, and missed
    /// calls on their extension. Every ring counts here, the rings of an
    /// abandoned call too (S-55): each was a ring on this agent's phone that
    /// they did not take.
    /// </summary>
    public async Task<IReadOnlyList<AgentProductivityRowDto>> AgentsAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var cells = await cube.CountAsync(Calls(filter) with { WithRings = true }, CubeBy.Agent | Outcome, ct);
        var names = await cube.NamesAsync(ct);

        return cells
            .Where(c => c.AgentId is not null)
            .GroupBy(c => c.AgentId!.Value)
            .Select(g =>
            {
                var answered = Count(g, IsAnswered);
                return new AgentProductivityRowDto(
                    g.Key,
                    names.Agent(g.Key),
                    answered,
                    Count(g, c => IsInbound(c) && IsAnswered(c)),
                    Count(g, IsOutbound),
                    answered == 0 ? null : (int)Math.Round((double)g.Sum(c => c.AnsweredSeconds) / answered),
                    g.Sum(c => c.Orders),
                    g.Sum(c => c.OrderValue),
                    g.Sum(Unclassified),
                    Count(g, IsMissed));
            })
            .OrderByDescending(r => r.Handled).ThenBy(r => r.Agent)
            .ToList();
    }

    // ---- R-16 -------------------------------------------------------------------------

    /// <summary>
    /// R-16: customers per day, week or month, new and returning. New means
    /// somebody here saved the contact in that bucket; the 15,289 customers
    /// carried over from the old system are always returning, which is what
    /// they are (<see cref="ReportCube.CustomersPerBucketAsync"/>).
    /// </summary>
    public async Task<IReadOnlyList<CustomerBaseRowDto>> CustomerBaseAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default) =>
        (await cube.CustomersPerBucketAsync(Calls(filter), TimeGrouping(groupBy), ct))
            .Select(b => new CustomerBaseRowDto(b.Bucket, b.Customers, b.New, b.Customers - b.New))
            .ToList();

    /// <summary>R-16: the customers with the most orders, or the most order value, in the period.</summary>
    /// <param name="by"><c>orders</c> (the default) or <c>value</c>.</param>
    public async Task<IReadOnlyList<CustomerRankRowDto>> TopCustomersAsync(
        ReportFilter filter, string? by, int top = 50, CancellationToken ct = default)
    {
        var customers = (await CustomersAsync(Calls(filter), ct)).Where(c => c.Orders > 0);
        return (by?.ToLowerInvariant() == "value"
                ? customers.OrderByDescending(c => c.OrderValue).ThenByDescending(c => c.Orders)
                : customers.OrderByDescending(c => c.Orders).ThenByDescending(c => c.OrderValue))
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// R-16's win-back list: customers who have ordered, but not in the last
    /// <paramref name="days"/> days, most orders first. <b>Not limited to the
    /// period</b>: "no order in 30 days" is measured back from today, over
    /// every order they ever placed. The agent and branch filters still apply.
    /// </summary>
    public async Task<IReadOnlyList<CustomerRankRowDto>> InactiveCustomersAsync(
        ReportFilter filter, int days, CancellationToken ct = default)
    {
        var since = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(days, 1, 3650));
        var cells = await cube.CountAsync(Calls(filter) with { From = null, To = null }, CubeBy.Contact, ct);

        return cells
            .Where(c => c.ContactId is not null && c.Orders > 0 && c.LastOrderAt < since)
            .Select(c => new CustomerRankRowDto(c.ContactId!.Value, c.ContactName, c.Number, c.N, c.Orders, c.OrderValue,
                c.Complaints, c.LastOrderAt!.Value))
            .OrderByDescending(c => c.Orders).ThenBy(c => c.LastAt)
            .ToList();
    }

    // ---- R-18 -------------------------------------------------------------------------

    /// <summary>
    /// R-18's figures: answered calls left unclassified and calls from numbers
    /// nobody saved, in the period; and names shared by more than one contact,
    /// which has no period.
    /// </summary>
    public async Task<DataQualityDto> DataQualityAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var outcome = await cube.CountAsync(Calls(filter), Outcome, ct);
        var unknown = await cube.CountAsync(Calls(filter), CubeBy.Number, ct);

        return new DataQualityDto(
            outcome.Sum(Unclassified),
            unknown.Sum(c => c.N),
            unknown.Count,
            (await DuplicateNamesQuery().CountAsync(ct)));
    }

    /// <summary>R-18: the numbers nobody saved, most calls first.</summary>
    public async Task<IReadOnlyList<UnknownNumberRowDto>> UnknownNumbersAsync(ReportFilter filter, CancellationToken ct = default) =>
        (await cube.CountAsync(Calls(filter), CubeBy.Number, ct))
            .Select(c => new UnknownNumberRowDto(c.Number!, c.N, c.FirstAt!.Value, c.LastAt!.Value))
            .OrderByDescending(r => r.Calls).ThenByDescending(r => r.LastAt)
            .ToList();

    /// <summary>
    /// R-18: names more than one contact shares, normalised as the name search
    /// normalises them (ة and ه alike, A-80), with the numbers on each. A
    /// shared name is a question, not an answer: common names are common, and
    /// merging is a deliberate act (A-63). The same number cannot be on two
    /// contacts (the unique index), so it is never a reason.
    /// </summary>
    public async Task<IReadOnlyList<DuplicateNameRowDto>> DuplicateNamesAsync(CancellationToken ct = default)
    {
        var names = await DuplicateNamesQuery().ToListAsync(ct);
        var contacts = await db.Contacts.AsNoTracking()
            .Where(c => c.DeletedAt == null && c.NameNormalised != null && names.Contains(c.NameNormalised))
            .Select(c => new { c.NameNormalised, c.Name, Numbers = c.Phones.Select(p => p.Raw).ToList() })
            .ToListAsync(ct);

        return contacts
            .GroupBy(c => c.NameNormalised!)
            .Select(g => new DuplicateNameRowDto(
                g.First().Name ?? g.Key,
                g.Count(),
                string.Join(" · ", g.SelectMany(c => c.Numbers))))
            .OrderByDescending(r => r.Contacts).ThenBy(r => r.Name)
            .ToList();
    }

    private IQueryable<string> DuplicateNamesQuery() => db.Contacts.AsNoTracking()
        .Where(c => c.DeletedAt == null && c.NameNormalised != null && c.NameNormalised != "")
        .GroupBy(c => c.NameNormalised!)
        .Where(g => g.Count() > 1)
        .Select(g => g.Key);

    // ---- R-20 -------------------------------------------------------------------------

    /// <summary>
    /// R-20: abandoned calls per day, week, month or hour of the day: how many,
    /// their share of incoming calls, how long those callers waited, and how
    /// many were called back and how soon (S-55). Only what the PBX import has
    /// saved: the report is as current as the last check.
    /// </summary>
    public async Task<IReadOnlyList<AbandonedRowDto>> AbandonedAsync(
        ReportFilter filter, string? groupBy, CancellationToken ct = default)
    {
        var by = (groupBy ?? string.Empty).ToLowerInvariant() is "week" or "month" or "hour" ? groupBy!.ToLowerInvariant() : "day";
        var names = await cube.NamesAsync(ct);
        var inbound = (await cube.CountAsync(Calls(filter), Outcome | Dimension(by), ct))
            .Where(IsInbound)
            .GroupBy(Heading(by, names))
            .ToDictionary(g => g.Key, g => g.Sum(c => c.N));

        var calls = (await AbandonedListAsync(filter, ct))
            .GroupBy(r =>
            {
                var bucket = ReportScope.Bucket(ReportScope.Local(r.StartedAt), by);
                return (Key: bucket, Label: by == "hour" ? $"{bucket}:00" : bucket);
            })
            .ToDictionary(g => g.Key, g => g.ToList());

        return inbound.Keys.Union(calls.Keys)
            .Select(heading =>
            {
                var rows = calls.GetValueOrDefault(heading) ?? [];
                var waits = rows.Where(r => r.WaitSec is not null).Select(r => r.WaitSec!.Value).ToList();
                var back = rows.Where(r => r.MinutesToCallBack is not null).Select(r => r.MinutesToCallBack!.Value).ToList();
                var inCount = inbound.GetValueOrDefault(heading);
                return new AbandonedRowDto(
                    heading.Key,
                    heading.Label,
                    inCount,
                    rows.Count,
                    Percent(rows.Count, inCount),
                    waits.Count == 0 ? null : (int)Math.Round(waits.Average()),
                    waits.Count == 0 ? null : waits.Max(),
                    back.Count,
                    back.Count == 0 ? null : (int)Math.Round(back.Average()));
            })
            .OrderBy(r => r.Key)
            .ToList();
    }

    /// <summary>
    /// R-20's list: every abandoned call in the period, newest first, with how
    /// long the caller waited, how often the agents' phones rang with it, and
    /// the first call anybody here made to that number afterwards (S-51's
    /// call-back, until call-back tasks exist).
    /// </summary>
    public async Task<IReadOnlyList<AbandonedCallRowDto>> AbandonedListAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var q = ReportScope.Narrow(db.Communications.AsNoTracking(), Calls(filter), await ReportScope.InternalNumbersAsync(db, ct))
            .Where(c => c.Direction == Directions.In && c.Status == CommunicationStatuses.Abandoned);

        var rows = await q
            .OrderByDescending(c => c.StartedAt).ThenBy(c => c.Id)
            .Select(c => new
            {
                c.Id,
                c.StartedAt,
                c.EndedAt,
                c.WaitSec,
                c.QueueName,
                c.RemoteNumberRaw,
                c.ContactId,
                Customer = c.Contact != null ? c.Contact.Name : null,
                Rings = db.Communications.Count(r => r.AbandonedCallId == c.Id),
                BackAt = db.Communications
                    .Where(o => o.Kind == CommunicationKinds.Call && o.Direction == Directions.Out
                        && c.RemoteNormalised != null && o.RemoteNormalised == c.RemoteNormalised
                        && o.StartedAt >= (c.EndedAt ?? c.StartedAt))
                    .OrderBy(o => o.StartedAt)
                    .Select(o => (DateTimeOffset?)o.StartedAt)
                    .FirstOrDefault(),
                BackBy = db.Communications
                    .Where(o => o.Kind == CommunicationKinds.Call && o.Direction == Directions.Out
                        && c.RemoteNormalised != null && o.RemoteNormalised == c.RemoteNormalised
                        && o.StartedAt >= (c.EndedAt ?? c.StartedAt))
                    .OrderBy(o => o.StartedAt)
                    .Select(o => o.Agent != null ? o.Agent.DisplayName : null)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new AbandonedCallRowDto(
                r.Id, r.StartedAt, r.EndedAt, r.WaitSec, r.QueueName, r.RemoteNumberRaw, r.ContactId, r.Customer, r.Rings,
                r.BackAt, r.BackBy,
                r.BackAt is { } at ? (int)Math.Max(0, Math.Round((at - (r.EndedAt ?? r.StartedAt)).TotalMinutes)) : null))
            .ToList();
    }

    // ---- S-20, the dashboard ---------------------------------------------------

    /// <summary>
    /// S-20's figures for the restaurant's today: calls and messages together,
    /// since the dashboard is the whole picture; missed and unclassified are
    /// calls' own words.
    /// </summary>
    public async Task<DashboardTodayDto> TodayAsync(CancellationToken ct = default)
    {
        var (start, end) = ReportScope.Today();
        var today = new ReportFilter(start, end, Kind: null);
        var cells = await cube.CountAsync(today, Outcome | CubeBy.Channel | CubeBy.Type, ct);
        var names = await cube.NamesAsync(ct);

        // Dia, 25 Sep: an open session. Closing the app does not close one,
        // so this includes an agent who went home without signing out.
        var online = await db.AgentSessions.AsNoTracking()
            .Where(s => s.LoggedOutAt == null && s.User.Role == UserRoles.Agent && s.User.IsActive)
            .Select(s => s.UserId)
            .Distinct()
            .CountAsync(ct);

        return new DashboardTodayDto(
            cells.Sum(c => c.N),
            Count(cells, IsCall),
            Count(cells, IsMessage),
            TypeCounts(cells, names),
            ChannelCounts(cells, names),
            cells.Sum(c => c.Orders),
            cells.Sum(c => c.OrderValue),
            cells.Sum(c => c.Complaints),
            Count(cells, IsMissed),
            cells.Sum(Unclassified),
            online,
            Count(cells, IsAbandoned));
    }

    /// <summary>
    /// S-20's four charts for the chosen period: communications per day (every
    /// day of the period, quiet ones as zero, so the line does not skip them),
    /// per type, per channel, and per hour of the day (all 24).
    /// </summary>
    public async Task<DashboardPeriodDto> PeriodAsync(ReportFilter filter, CancellationToken ct = default)
    {
        var f = CallsAndMessages(filter);
        var names = await cube.NamesAsync(ct);
        var perDay = (await cube.CountAsync(f, CubeBy.Day, ct)).ToDictionary(c => ReportScope.Day(c.Day!.Value), c => c.N);
        var perHour = (await cube.CountAsync(f, CubeBy.Hour, ct)).ToDictionary(c => c.Hour!.Value, c => c.N);
        var perType = await cube.CountAsync(f, CubeBy.Type, ct);
        var perChannel = await cube.CountAsync(f, CubeBy.Channel, ct);

        var days = new List<CountDto>();
        if (filter.From is { } from && filter.To is { } to && (to - from).TotalDays <= 400)
        {
            for (var day = ReportScope.Local(from).Date; day < ReportScope.Local(to); day = day.AddDays(1))
            {
                var key = ReportScope.Day(day);
                days.Add(new CountDto(key, key, perDay.GetValueOrDefault(key)));
            }
        }
        else
        {
            days.AddRange(perDay.OrderBy(d => d.Key).Select(d => new CountDto(d.Key, d.Key, d.Value)));
        }

        return new DashboardPeriodDto(
            days,
            TypeCounts(perType, names),
            ChannelCounts(perChannel, names),
            Enumerable.Range(0, 24)
                .Select(h => new CountDto(h.ToString("00"), $"{h:00}:00", perHour.GetValueOrDefault(h)))
                .ToList());
    }

    // ---- the groupings ----------------------------------------------------------------

    private static bool IsTime(string groupBy) => groupBy.ToLowerInvariant() is "day" or "week" or "month" or "hour";

    /// <summary>Day, week or month; anything else is a day.</summary>
    private static string TimeGrouping(string? groupBy) =>
        (groupBy ?? string.Empty).ToLowerInvariant() is "week" or "month" ? groupBy!.ToLowerInvariant() : "day";

    /// <summary>What the cube must group by for a heading.</summary>
    private static CubeBy Dimension(string groupBy) => groupBy.ToLowerInvariant() switch
    {
        "branch" => CubeBy.Branch,
        "agent" => CubeBy.Agent,
        "channel" => CubeBy.Channel,
        "hour" => CubeBy.Hour,
        _ => CubeBy.Day,
    };

    /// <summary>What a cell is grouped under: a period bucket, a branch, an agent or a channel.</summary>
    private static Func<CubeCell, (string Key, string Label)> Heading(string groupBy, ReportNames names) => groupBy.ToLowerInvariant() switch
    {
        "branch" => c => (c.BranchId?.ToString() ?? string.Empty, names.Branch(c.BranchId)),
        "agent" => c => (c.AgentId?.ToString() ?? string.Empty, names.Agent(c.AgentId)),
        "channel" => c => (c.ChannelId?.ToString() ?? string.Empty, names.Channel(c.ChannelId)),
        "hour" => c => (c.Hour!.Value.ToString("00"), $"{c.Hour!.Value:00}:00"),
        var time => c =>
        {
            var bucket = ReportScope.Bucket(c.Day!.Value, time);
            return (bucket, bucket);
        },
    };

    private static IReadOnlyList<TypeCountDto> TypeCounts(IEnumerable<CubeCell> cells, ReportNames names) => cells
        .Where(c => c.TypeId is not null)
        .GroupBy(c => c.TypeId!.Value)
        .Select(g => (Type: names.Types[g.Key], Count: g.Sum(c => c.N)))
        .Select(x => new TypeCountDto(x.Type.Name, x.Type.LabelAr, x.Type.LabelEn, x.Count))
        .OrderByDescending(t => t.Count).ThenBy(t => t.TypeName)
        .ToList();

    private static IReadOnlyList<CountDto> ChannelCounts(IEnumerable<CubeCell> cells, ReportNames names) => cells
        .GroupBy(c => c.ChannelId!.Value)
        .Select(g => new CountDto(g.Key.ToString(), names.Channel(g.Key), g.Sum(c => c.N)))
        .OrderByDescending(c => c.Count).ThenBy(c => c.Label)
        .ToList();

    /// <summary>
    /// One line per customer. A customer is a contact: a call from a number
    /// nobody saved is not counted as anybody's (R-04, R-16).
    /// </summary>
    private async Task<IEnumerable<CustomerRankRowDto>> CustomersAsync(ReportFilter f, CancellationToken ct) =>
        (await cube.CountAsync(f, CubeBy.Contact, ct))
            .Where(c => c.ContactId is not null)
            .Select(c => new CustomerRankRowDto(c.ContactId!.Value, c.ContactName, c.Number, c.N, c.Orders, c.OrderValue,
                c.Complaints, c.LastAt!.Value));

    /// <summary>A share as a percentage to one decimal, or null when there is nothing to share.</summary>
    private static decimal? Percent(int part, int whole) => whole == 0 ? null : Math.Round(part * 100m / whole, 1);
}
