using System.Globalization;
using System.Text;

namespace CallCenter.Server.Features.Pbx;

/// <summary>
/// One call the PBX gave up on waiting for an agent (S-55), from its Calls
/// Detail report. Times are the PBX's local time.
/// </summary>
/// <param name="EndedAt">When the caller hung up.</param>
/// <param name="WaitSec">How long they waited in the queue first.</param>
public sealed record AbandonedRow(DateTime EndedAt, int WaitSec, string Queue, string Phone)
{
    /// <summary>When the caller joined the queue: the hang-up less the wait.</summary>
    public DateTime QueuedAt => EndedAt.AddSeconds(-WaitSec);

    /// <summary>
    /// The call's identity, stored as <c>pbx_unique_id</c>. The report has no
    /// call id, so it is the hang-up time to the second, the number and the
    /// queue: the same row downloaded again has the same key, and two calls
    /// cannot share one unless the same caller gave up twice in the same second.
    /// </summary>
    public string Key => string.Create(CultureInfo.InvariantCulture,
        $"issabel:{EndedAt:yyyy-MM-dd HH:mm:ss}|{Phone}|{Queue}");
}

/// <summary>
/// Reads the Calls Detail CSV (S-55). The header, as the PBX sent it on 26 Sep:
/// <c>"No. Agent","Agent","Start Time","End Time","Duration","Duration Wait","Queue","Type","Phone","Transfer","Status",</c>
/// </summary>
/// <remarks>
/// <b>Abandoned is the PBX's own word.</b> The Status column says Success or
/// Abandoned; nothing here decides which calls count. An abandoned row has no
/// agent and no start time - it never started - and its End Time is the
/// hang-up.
///
/// Columns are found by name, not position, so a PBX update that adds one does
/// not shift the others. The names are the English ones: the PBX writes the
/// headings in the language of the user the server logs in as, and a user set
/// to another language gets a clear error rather than an empty import.
/// </remarks>
public static class CallsDetailCsv
{
    public const string StatusAbandoned = "Abandoned";
    public const string TypeIncoming = "Incoming";

    /// <summary>What was in a download: how many calls, and the abandoned incoming ones.</summary>
    public sealed record Parsed(int Calls, IReadOnlyList<AbandonedRow> Abandoned);

    /// <exception cref="PbxImportException">The file is not the Calls Detail report.</exception>
    public static Parsed Parse(string csv)
    {
        var lines = ReadRecords(csv.TrimStart('﻿')).Where(r => r.Any(f => f.Length > 0)).ToList();
        if (lines.Count == 0)
        {
            throw new PbxImportException("The PBX sent an empty file instead of the Calls Detail report.");
        }

        var header = lines[0];
        int Column(string name)
        {
            var at = header.FindIndex(h => string.Equals(h.Trim(), name, StringComparison.OrdinalIgnoreCase));
            return at >= 0
                ? at
                : throw new PbxImportException(
                    $"The PBX's report has no '{name}' column. The server reads the English headings: "
                    + "set this PBX user's language to English. "
                    + $"The headings it sent: {string.Join(", ", header.Take(12).Select(h => h.Length > 30 ? h[..30] : h))}");
        }

        var end = Column("End Time");
        var wait = Column("Duration Wait");
        var queue = Column("Queue");
        var type = Column("Type");
        var phone = Column("Phone");
        var status = Column("Status");

        var abandoned = new List<AbandonedRow>();
        foreach (var row in lines.Skip(1))
        {
            string Field(int i) => i < row.Count ? row[i].Trim() : string.Empty;

            if (!string.Equals(Field(status), StatusAbandoned, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Field(type), TypeIncoming, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // A caller still waiting has no hang-up yet. The next check has them.
            if (!DateTime.TryParseExact(Field(end), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var endedAt))
            {
                continue;
            }

            abandoned.Add(new AbandonedRow(endedAt, Seconds(Field(wait)), Field(queue), Field(phone)));
        }

        return new Parsed(lines.Count - 1, abandoned);
    }

    /// <summary><c>hh:mm:ss</c>, hours past 24 allowed; anything unreadable is no wait.</summary>
    public static int Seconds(string value)
    {
        var parts = value.Split(':');
        return parts.Length == 3
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var s)
            ? h * 3600 + m * 60 + s
            : 0;
    }

    /// <summary>RFC 4180 records: fields in double quotes, "" for a quote, line breaks inside quotes kept.</summary>
    private static IEnumerable<List<string>> ReadRecords(string csv)
    {
        var record = new List<string>();
        var field = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < csv.Length && csv[i + 1] == '"') { field.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else field.Append(ch);
                continue;
            }

            switch (ch)
            {
                case '"':
                    quoted = true;
                    break;
                case ',':
                    record.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    record.Add(field.ToString());
                    field.Clear();
                    yield return record;
                    record = [];
                    break;
                default:
                    field.Append(ch);
                    break;
            }
        }

        if (field.Length > 0 || record.Count > 0)
        {
            record.Add(field.ToString());
            yield return record;
        }
    }
}
