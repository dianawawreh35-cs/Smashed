namespace CallCenter.Server.Features.Reports;

/// <summary>
/// Which complaints are one complaint (Dia, 6 Oct 2026): everything classified
/// Complaint from one customer on one working day — the call in, the agent's
/// call back, a message through an application, in any order. A customer is
/// the same contact or the same number; two rows that share either, directly
/// or through a third, are one complaint.
/// </summary>
/// <remarks>
/// <b>A working day runs from 05:00 to 05:00</b>, because the restaurant is
/// open past midnight, until one or two and sometimes later: a complaint at
/// 01:30 and its call back at 11:00 the next morning are two working days, but
/// a complaint at 23:30 and a call back at 00:40 are one.
/// A row with neither a contact nor a number (a withheld caller nobody saved)
/// is a complaint of its own: there is nothing to join it to.
/// </remarks>
public static class ComplaintCases
{
    /// <summary>The hour a working day begins, in the restaurant's time.</summary>
    public const int WorkingDayStartsAtHour = 5;

    /// <summary>The working day an instant belongs to: before 05:00 is still the day before.</summary>
    public static DateTime WorkingDay(DateTimeOffset at) =>
        ReportScope.Local(at).AddHours(-WorkingDayStartsAtHour).Date;

    /// <summary>What the grouping looks at.</summary>
    public readonly record struct Key(DateTimeOffset At, Guid? ContactId, string? Number);

    /// <summary>
    /// The rows as complaints: each list in time order, the lists in the order
    /// their first rows came.
    /// </summary>
    public static List<List<T>> Group<T>(IReadOnlyList<T> rows, Func<T, Key> key)
    {
        var parent = Enumerable.Range(0, rows.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);

        var seen = new Dictionary<string, int>();
        for (var i = 0; i < rows.Count; i++)
        {
            var k = key(rows[i]);
            var day = WorkingDay(k.At).ToString("yyyy-MM-dd");
            if (k.ContactId is { } contact) Join($"c:{day}:{contact}", i);
            if (!string.IsNullOrEmpty(k.Number)) Join($"n:{day}:{k.Number}", i);
        }

        return Enumerable.Range(0, rows.Count)
            .GroupBy(Find)
            .Select(g => g.Select(i => rows[i]).OrderBy(r => key(r).At).ToList())
            .OrderBy(g => key(g[0]).At)
            .ToList();

        void Join(string name, int i)
        {
            if (seen.TryGetValue(name, out var other)) parent[Find(i)] = Find(other);
            else seen[name] = i;
        }
    }
}
