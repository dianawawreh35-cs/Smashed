using System.Text;
using CallCenter.Server.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace CallCenter.Server.Features.Reports;

/// <summary>What a report groups by. Each report asks only for the ones it needs, so the answer stays small.</summary>
[Flags]
public enum CubeBy
{
    None = 0,
    /// <summary>The restaurant's local date.</summary>
    Day = 1 << 0,
    /// <summary>The restaurant's local hour of the day, 0–23.</summary>
    Hour = 1 << 1,
    Kind = 1 << 2,
    Direction = 1 << 3,
    Status = 1 << 4,
    Channel = 1 << 5,
    Branch = 1 << 6,
    Agent = 1 << 7,
    Type = 1 << 8,
    /// <summary>The contact, with its name and the number last used. Calls from unsaved numbers group under null.</summary>
    Contact = 1 << 9,
    /// <summary>
    /// The normalised number, and only for calls from a number nobody saved
    /// (R-18): asking for it narrows the rows to those.
    /// </summary>
    Number = 1 << 10,
}

/// <summary>
/// One cell of the cube: the dimensions asked for (the rest null) and what was
/// counted in it. The query names its columns exactly as these properties are
/// spelt: a raw query's result type is not part of the model, so the
/// snake-case convention in <c>CallCenterDbContext</c> does not reach it.
/// </summary>
public sealed class CubeCell
{
    public DateTime? Day { get; init; }
    public int? Hour { get; init; }
    public string? Kind { get; init; }
    public string? Direction { get; init; }
    public string? Status { get; init; }
    public Guid? ChannelId { get; init; }
    public Guid? BranchId { get; init; }
    public Guid? AgentId { get; init; }
    public Guid? TypeId { get; init; }
    public Guid? ContactId { get; init; }
    public string? ContactName { get; init; }
    public string? Number { get; init; }

    /// <summary>Communications in the cell.</summary>
    public int N { get; init; }
    public int Classified { get; init; }
    public int Orders { get; init; }
    public decimal OrderValue { get; init; }
    public int Complaints { get; init; }
    public int Cancellations { get; init; }
    /// <summary>Complaints the agent ticked for a follow-up.</summary>
    public int FollowUp { get; init; }
    /// <summary>Complaints a supervisor marked resolved.</summary>
    public int Resolved { get; init; }
    /// <summary>Summed hours from call to resolution, over the resolved complaints that have a time.</summary>
    public double ResolveHours { get; init; }
    public int ResolvedTimed { get; init; }
    /// <summary>Summed talk time over answered calls, in seconds.</summary>
    public long AnsweredSeconds { get; init; }
    public DateTimeOffset? FirstAt { get; init; }
    public DateTimeOffset? LastAt { get; init; }
    /// <summary>The last communication classified as an order (R-16's inactive customers).</summary>
    public DateTimeOffset? LastOrderAt { get; init; }
}

/// <summary>The names the cube's ids stand for: small tables, read once per report.</summary>
public sealed record ReportNames(
    IReadOnlyDictionary<Guid, string> Branches,
    IReadOnlyDictionary<Guid, string> Agents,
    IReadOnlyDictionary<Guid, string> Channels,
    IReadOnlyDictionary<Guid, (string Name, string LabelAr, string LabelEn)> Types)
{
    public string Branch(Guid? id) => id is { } b && Branches.TryGetValue(b, out var n) ? n : "—";
    public string Agent(Guid? id) => id is { } a && Agents.TryGetValue(a, out var n) ? n : "—";
    public string Channel(Guid? id) => id is { } c && Channels.TryGetValue(c, out var n) ? n : "—";
    public string? TypeName(Guid? id) => id is { } t && Types.TryGetValue(t, out var n) ? n.Name : null;
}

/// <summary>
/// The call and application reports' figures, counted by PostgreSQL (N-02).
/// </summary>
/// <remarks>
/// <b>Why SQL.</b> The first version fetched every row the filters named and
/// grouped them here. Measured on 26 Sep against a year of synthetic data
/// (180,310 communications, <c>tools/report-probe</c>), every report that read
/// the whole year took 3 to 4.4 seconds — inside N-02's five, but over the two
/// the task set, and the time was all in moving 180,000 wide rows. Grouped in
/// the database, each report moves a few hundred.
///
/// <b>The restaurant's local time is still decided here, not by PostgreSQL.</b>
/// .NET works out the UTC offset for each stretch of the period, clock changes
/// included (<see cref="Segments"/>), and the query adds the offset it is given.
/// PostgreSQL's own rules for Asia/Hebron never come into it, so a day in a
/// report is always the day the edit window (A-42) and the application
/// reports call it, even in a year the clocks change on a date announced late.
///
/// <b>The filters are <see cref="ReportScope.Narrow"/>'s, written in SQL</b>:
/// the same set, the same meaning — kind, the half-open period, agent, branch,
/// channel, type, and S-48's internal numbers with a withheld number let
/// through. Change one, change both; the acceptance test compares a list
/// (built by <c>Narrow</c>) with the figures (built here) over one known day.
///
/// <b>The words</b> — missed, answered, an order — are not here. The cube
/// counts by status, direction and type, and the services decide what those
/// mean, in one place each.
/// </remarks>
public class ReportCube(CallCenterDbContext db)
{
    /// <summary>The cells of <paramref name="by"/> for the rows <paramref name="f"/> names.</summary>
    public async Task<List<CubeCell>> CountAsync(ReportFilter f, CubeBy by, CancellationToken ct)
    {
        var internalNumbers = await ReportScope.InternalNumbersAsync(db, ct);
        var from = f.From ?? new DateTimeOffset(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var to = f.To ?? DateTimeOffset.UtcNow.AddDays(2);
        var segments = Segments(from, to);

        var keys = new List<(CubeBy Dim, string Select, string Group)>
        {
            (CubeBy.Day, "(c.started_at AT TIME ZONE 'UTC' + make_interval(mins => s.off))::date::timestamp", "1"),
            (CubeBy.Hour, "extract(hour from c.started_at AT TIME ZONE 'UTC' + make_interval(mins => s.off))::int", "2"),
            (CubeBy.Kind, "c.kind", "3"),
            (CubeBy.Direction, "c.direction", "4"),
            (CubeBy.Status, "c.status", "5"),
            (CubeBy.Channel, "c.channel_id", "6"),
            (CubeBy.Branch, "c.branch_id", "7"),
            (CubeBy.Agent, "c.agent_id", "8"),
            (CubeBy.Type, "cl.type_id", "9"),
            (CubeBy.Contact, "c.contact_id", "10"),
            (CubeBy.Number, "c.remote_normalised", "12"),
        };

        var select = new StringBuilder();
        var group = new List<string>();
        string Col(CubeBy dim, string expr, string type) => by.HasFlag(dim) ? expr : $"NULL::{type}";

        select.Append($"{Col(CubeBy.Day, keys[0].Select, "timestamp")} AS \"Day\", ");
        select.Append($"{Col(CubeBy.Hour, keys[1].Select, "int")} AS \"Hour\", ");
        select.Append($"{Col(CubeBy.Kind, "c.kind", "text")} AS \"Kind\", ");
        select.Append($"{Col(CubeBy.Direction, "c.direction", "text")} AS \"Direction\", ");
        select.Append($"{Col(CubeBy.Status, "c.status", "text")} AS \"Status\", ");
        select.Append($"{Col(CubeBy.Channel, "c.channel_id", "uuid")} AS \"ChannelId\", ");
        select.Append($"{Col(CubeBy.Branch, "c.branch_id", "uuid")} AS \"BranchId\", ");
        select.Append($"{Col(CubeBy.Agent, "c.agent_id", "uuid")} AS \"AgentId\", ");
        select.Append($"{Col(CubeBy.Type, "cl.type_id", "uuid")} AS \"TypeId\", ");
        select.Append($"{Col(CubeBy.Contact, "c.contact_id", "uuid")} AS \"ContactId\", ");
        // The name and the number last used, for the customer lists (R-04, R-05, R-16).
        select.Append(by.HasFlag(CubeBy.Contact) ? "max(ct.name) AS \"ContactName\", " : "NULL::text AS \"ContactName\", ");
        select.Append(by.HasFlag(CubeBy.Contact)
            ? "(array_agg(c.remote_number_raw ORDER BY c.started_at DESC))[1] AS \"Number\", "
            : by.HasFlag(CubeBy.Number) ? "c.remote_normalised AS \"Number\", " : "NULL::text AS \"Number\", ");

        foreach (var key in keys)
        {
            if (by.HasFlag(key.Dim)) group.Add(key.Group);
        }

        var sql = new StringBuilder();
        sql.Append("""
            WITH s AS (SELECT * FROM unnest(@seg_from, @seg_to, @seg_off) AS s(f, t, off))
            SELECT
            """);
        sql.Append(' ').Append(select);
        sql.Append("""
                count(*)::int AS "N",
                count(cl.communication_id)::int AS "Classified",
                (count(*) FILTER (WHERE ty.name = 'Order'))::int AS "Orders",
                coalesce(sum(cl.order_value) FILTER (WHERE ty.name = 'Order'), 0) AS "OrderValue",
                (count(*) FILTER (WHERE ty.name = 'Complaint'))::int AS "Complaints",
                (count(*) FILTER (WHERE ty.name = 'Cancellation'))::int AS "Cancellations",
                (count(*) FILTER (WHERE ty.name = 'Complaint' AND cl.follow_up))::int AS "FollowUp",
                (count(*) FILTER (WHERE ty.name = 'Complaint' AND cl.resolved))::int AS "Resolved",
                coalesce(sum(extract(epoch FROM cl.resolved_at - c.started_at) / 3600.0)
                    FILTER (WHERE ty.name = 'Complaint' AND cl.resolved AND cl.resolved_at IS NOT NULL), 0)::float8 AS "ResolveHours",
                (count(*) FILTER (WHERE ty.name = 'Complaint' AND cl.resolved AND cl.resolved_at IS NOT NULL))::int AS "ResolvedTimed",
                coalesce(sum(c.duration_sec) FILTER (WHERE c.status = 'Answered'), 0)::bigint AS "AnsweredSeconds",
                min(c.started_at) AS "FirstAt",
                max(c.started_at) AS "LastAt",
                max(c.started_at) FILTER (WHERE ty.name = 'Order') AS "LastOrderAt"
            FROM communications c
            JOIN s ON c.started_at >= s.f AND c.started_at < s.t
            LEFT JOIN classifications cl ON cl.communication_id = c.id
            LEFT JOIN classification_types ty ON ty.id = cl.type_id
            """);
        if (by.HasFlag(CubeBy.Contact)) sql.Append(" LEFT JOIN contacts ct ON ct.id = c.contact_id");

        var parameters = new List<NpgsqlParameter>
        {
            new("seg_from", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = segments.Select(x => x.From.UtcDateTime).ToArray() },
            new("seg_to", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = segments.Select(x => x.To.UtcDateTime).ToArray() },
            new("seg_off", NpgsqlDbType.Array | NpgsqlDbType.Integer) { Value = segments.Select(x => x.OffsetMinutes).ToArray() },
        };

        var where = Where(f, internalNumbers, parameters);
        if (by.HasFlag(CubeBy.Number)) where.Add("c.contact_id IS NULL AND c.remote_normalised IS NOT NULL");

        sql.Append(" WHERE ").Append(string.Join(" AND ", where));
        if (group.Count > 0) sql.Append(" GROUP BY ").Append(string.Join(", ", group));

        return await WithRoomAsync(() => db.Database
            .SqlQueryRaw<CubeCell>(sql.ToString(), parameters.Cast<object>().ToArray())
            .ToListAsync(ct), ct);
    }

    /// <summary>
    /// Runs a report query with enough working memory to group in memory.
    /// </summary>
    /// <remarks>
    /// With PostgreSQL's default 4 MB the planner sorts a year of rows to group
    /// them and the sort spills to disk: 1.1 s for R-01 on the probe's year.
    /// With 64 MB it hashes in memory: 0.34 s (26 Sep). Set for this connection
    /// only, and only while the query runs: Npgsql resets a pooled connection's
    /// session (DISCARD ALL) when it is handed back, so nothing else inherits it.
    /// </remarks>
    private async Task<T> WithRoomAsync<T>(Func<Task<T>> query, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("SET work_mem = '64MB'", ct);
            return await query();
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    /// <summary>
    /// R-16: per bucket, how many customers called, and how many of them were
    /// new — saved by somebody here in that bucket or after it (a contact
    /// saved from the pop-up takes its earlier calls with it, A-11). A contact
    /// nobody saved here, <c>created_by</c> empty, came with the old system's
    /// customer book (the seed) and is never new: they were customers before
    /// this system existed, and "saved in September" would otherwise make all
    /// 15,289 of them new in the month it went live. The buckets are the
    /// restaurant's days, weeks or months, cut in .NET and passed as instants,
    /// for the same reason the segments are.
    /// </summary>
    public async Task<List<(string Bucket, int Customers, int New)>> CustomersPerBucketAsync(
        ReportFilter f, string? grouping, CancellationToken ct)
    {
        var internalNumbers = await ReportScope.InternalNumbersAsync(db, ct);
        var from = ReportScope.Local(f.From ?? DateTimeOffset.UtcNow.AddYears(-1)).Date;
        var to = f.To ?? DateTimeOffset.UtcNow.AddDays(1);
        var unit = (grouping ?? "day").ToLowerInvariant();

        var buckets = new List<(DateTimeOffset From, DateTimeOffset To, string Key)>();
        var cursor = unit switch
        {
            "week" => ReportScope.StartOfWeek(from),
            "month" => new DateTime(from.Year, from.Month, 1),
            _ => from,
        };
        while (At(cursor) < to && buckets.Count < 5000)
        {
            var next = unit switch { "week" => cursor.AddDays(7), "month" => cursor.AddMonths(1), _ => cursor.AddDays(1) };
            buckets.Add((At(cursor), At(next), ReportScope.Bucket(cursor, unit)));
            cursor = next;
        }

        var parameters = new List<NpgsqlParameter>
        {
            new("b_from", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = buckets.Select(b => b.From.UtcDateTime).ToArray() },
            new("b_to", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = buckets.Select(b => b.To.UtcDateTime).ToArray() },
            new("b_key", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = buckets.Select(b => b.Key).ToArray() },
        };
        var where = Where(f, internalNumbers, parameters);

        // The period is the buckets'; a bucket cut by the period's own edges
        // still counts only the calls inside the period.
        where.Add("c.contact_id IS NOT NULL");
        if (f.From is { } start) { where.Add("c.started_at >= @p_from"); parameters.Add(new("p_from", start.UtcDateTime)); }
        if (f.To is { } end) { where.Add("c.started_at < @p_to"); parameters.Add(new("p_to", end.UtcDateTime)); }

        var sql = $"""
            WITH b AS (SELECT * FROM unnest(@b_from, @b_to, @b_key) AS b(f, t, k))
            SELECT b.k AS "Bucket",
                   count(DISTINCT c.contact_id)::int AS "Customers",
                   (count(DISTINCT c.contact_id) FILTER (WHERE ct.created_by IS NOT NULL AND ct.created_at >= b.f))::int AS "New"
            FROM communications c
            JOIN b ON c.started_at >= b.f AND c.started_at < b.t
            JOIN contacts ct ON ct.id = c.contact_id
            LEFT JOIN classifications cl ON cl.communication_id = c.id
            WHERE {string.Join(" AND ", where)}
            GROUP BY b.k
            ORDER BY b.k
            """;

        var rows = await WithRoomAsync(() => db.Database
            .SqlQueryRaw<BucketCustomers>(sql, parameters.Cast<object>().ToArray()).ToListAsync(ct), ct);
        return rows.Select(r => (r.Bucket, r.Customers, r.New)).ToList();

        static DateTimeOffset At(DateTime local) => new(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class BucketCustomers
    {
        public string Bucket { get; init; } = "";
        public int Customers { get; init; }
        public int New { get; init; }
    }

    /// <summary>The names behind the ids in a cell.</summary>
    public async Task<ReportNames> NamesAsync(CancellationToken ct) => new(
        await db.Branches.AsNoTracking().ToDictionaryAsync(b => b.Id, b => b.Name, ct),
        await db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.DisplayName, ct),
        await db.Channels.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct),
        await db.ClassificationTypes.AsNoTracking().ToDictionaryAsync(t => t.Id, t => (t.Name, t.LabelAr, t.LabelEn), ct));

    /// <summary>
    /// <see cref="ReportScope.Narrow"/>'s filters, the same set with the same
    /// meaning, in SQL — all but the period, which each query joins on its own
    /// way. Classifications are joined as <c>cl</c>.
    /// </summary>
    private static List<string> Where(ReportFilter f, IReadOnlyList<string> internalNumbers, List<NpgsqlParameter> parameters)
    {
        var where = new List<string> { "true" };
        if (f.Kind is { } kind) { where.Add("c.kind = @kind"); parameters.Add(new("kind", kind)); }
        if (f.AgentId is { } agent) { where.Add("c.agent_id = @agent"); parameters.Add(new("agent", agent)); }
        if (f.BranchId is { } branch) { where.Add("c.branch_id = @branch"); parameters.Add(new("branch", branch)); }
        if (f.ChannelId is { } channel) { where.Add("c.channel_id = @channel"); parameters.Add(new("channel", channel)); }
        if (f.TypeId is { } type) { where.Add("cl.type_id = @type"); parameters.Add(new("type", type)); }
        if (internalNumbers.Count > 0)
        {
            // S-48. A withheld number is a customer's, so NULL is let through.
            where.Add("(c.remote_normalised IS NULL OR NOT c.remote_normalised = ANY(@internal))");
            parameters.Add(new("internal", NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = internalNumbers.ToArray() });
        }

        return where;
    }

    /// <summary>One stretch of the period with a single UTC offset, as .NET's time zone rules have it.</summary>
    public sealed record Segment(DateTimeOffset From, DateTimeOffset To, int OffsetMinutes);

    /// <summary>
    /// The period cut where the restaurant's clocks change. Walked an hour at a
    /// time (a year is 8,760 steps) and each change pinned to the minute, since
    /// a clock change is not always on the hour's UTC boundary.
    /// </summary>
    public static IReadOnlyList<Segment> Segments(DateTimeOffset from, DateTimeOffset to)
    {
        var zone = TimeZoneInfo.Local;
        int Offset(DateTimeOffset at) => (int)zone.GetUtcOffset(at).TotalMinutes;

        var list = new List<Segment>();
        var start = from.ToUniversalTime();
        var end = to.ToUniversalTime();
        if (end <= start) return [new Segment(start, start.AddTicks(1), Offset(start))];

        var current = Offset(start);
        var cursor = start;
        while (cursor < end)
        {
            var next = cursor.AddHours(1) < end ? cursor.AddHours(1) : end;
            if (Offset(next) != current && next < end)
            {
                // Narrow the change to the minute between cursor and next.
                var lo = cursor;
                var hi = next;
                while (hi - lo > TimeSpan.FromMinutes(1))
                {
                    var mid = lo + (hi - lo) / 2;
                    if (Offset(mid) == current) lo = mid; else hi = mid;
                }

                var change = new DateTimeOffset(hi.UtcDateTime.Ticks - hi.UtcDateTime.Ticks % TimeSpan.TicksPerMinute, TimeSpan.Zero);
                if (Offset(change) == current) change = change.AddMinutes(1);
                list.Add(new Segment(list.Count == 0 ? start : list[^1].To, change, current));
                current = Offset(change);
            }

            cursor = next;
        }

        list.Add(new Segment(list.Count == 0 ? start : list[^1].To, end, current));
        return list;
    }
}
