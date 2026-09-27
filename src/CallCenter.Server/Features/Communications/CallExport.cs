using System.Globalization;
using System.Text;
using CallCenter.Shared.Contracts.Communications;

namespace CallCenter.Server.Features.Communications;

/// <summary>
/// R-02 as a file (S-05): the call search's rows, every one of them, as CSV
/// that Excel opens with the Arabic intact.
/// </summary>
/// <remarks>
/// <b>Built here, not in the browser</b>, because this is a list and the
/// browser only ever holds a page of it; an export of the page on screen would
/// lie the way filtering one does (20 Sep). The report cards export in the
/// browser because their rows are the whole report.
///
/// <b>The same file the browser writes</b> (<c>src/lib/csv.ts</c>): a
/// byte-order mark, CRLF, RFC 4180 quoting, numbers with a dot. The headings
/// and the words for a result or a direction are the web app's own, in the
/// language the supervisor is reading in; change one, change both.
///
/// <b>Nothing in a cell runs as a formula</b> (M-S02, 27 Sep review). Names come
/// from the agents and from the POS, so a cell starting with <c>=</c>, <c>+</c>,
/// <c>-</c>, <c>@</c>, a tab or a carriage return is written with a leading
/// apostrophe, which Excel shows and does not run. <b>The number column is
/// different on purpose</b> (Dia, 27 Sep): as plain text Excel turned
/// <c>0599123456</c> into <c>599123456</c> and read <c>+970599123456</c> as a
/// sum, and an apostrophe would show on every row. So a number made only of
/// digits, spaces, <c>+</c>, <c>-</c> and brackets is written as
/// <c>="0599123456"</c>, which Excel shows exactly as typed, as text; anything
/// else in that column is treated like any other cell.
/// </remarks>
public static class CallExport
{
    public const string ContentType = "text/csv; charset=utf-8";

    private static readonly Dictionary<string, string[]> Headings = new()
    {
        ["en"] = ["Date", "Time", "Direction", "Result", "Agent", "Customer", "Number", "Branch", "Channel",
            "Type", "Order value", "Duration (seconds)", "Notes", "Recording"],
        ["ar"] = ["التاريخ", "الوقت", "الاتجاه", "النتيجة", "الموظف", "الزبون", "الرقم", "الفرع", "القناة",
            "النوع", "قيمة الطلب", "المدة (ثوانٍ)", "ملاحظات", "التسجيل"],
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Words = new()
    {
        ["en"] = new()
        {
            ["In"] = "Incoming", ["Out"] = "Outgoing", ["None"] = "",
            ["Answered"] = "Answered", ["Missed"] = "Missed", ["Rejected"] = "Rejected", ["Blocked"] = "Blocked",
            ["Abandoned"] = "Abandoned", ["Overflowed"] = "Overflowed", ["Failed"] = "Failed", ["Ringing"] = "Ringing",
            ["Logged"] = "Logged", ["NoAnswer"] = "Not answered",
            ["recorded"] = "Recorded", ["expired"] = "Expired",
        },
        ["ar"] = new()
        {
            ["In"] = "واردة", ["Out"] = "صادرة", ["None"] = "",
            ["Answered"] = "تم الرد", ["Missed"] = "فائتة", ["Rejected"] = "مرفوضة", ["Blocked"] = "محظورة",
            ["Abandoned"] = "متروكة", ["Overflowed"] = "محوّلة", ["Failed"] = "فاشلة", ["Ringing"] = "ترنّ",
            ["Logged"] = "مسجّلة", ["NoAnswer"] = "لم يُرَد",
            ["recorded"] = "مسجّلة", ["expired"] = "انتهت صلاحيته",
        },
    };

    /// <summary>Arabic unless English is asked for: Arabic is the app's first language (A-80).</summary>
    public static string Language(string? lang) => lang?.StartsWith("en", StringComparison.OrdinalIgnoreCase) == true ? "en" : "ar";

    /// <summary>Writes the heading and every row to <paramref name="output"/>, a line at a time.</summary>
    public static async Task WriteAsync(
        Stream output, IAsyncEnumerable<CallSearchRowDto> rows, string lang, CancellationToken ct)
    {
        var language = Language(lang);
        var words = Words[language];

        // UTF8Encoding(true) writes the byte-order mark: without it Excel reads
        // UTF-8 as Windows-1252 and every Arabic name is question marks.
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true);
        writer.NewLine = "\r\n";

        await writer.WriteLineAsync(Line(Headings[language]));

        await foreach (var r in rows.WithCancellation(ct))
        {
            var local = r.StartedAt.ToLocalTime();
            await writer.WriteLineAsync(Line(
            [
                local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                local.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                words.GetValueOrDefault(r.Direction, r.Direction),
                words.GetValueOrDefault(r.Status, r.Status),
                r.AgentDisplayName,
                r.ContactName,
                Number(r.RemoteNumberRaw),
                r.BranchName,
                r.ChannelName,
                language == "ar" ? r.TypeLabelAr : r.TypeLabelEn,
                r.OrderValue?.ToString(CultureInfo.InvariantCulture),
                r.DurationSec?.ToString(CultureInfo.InvariantCulture),
                r.Notes,
                r.HasRecording ? words["recorded"] : r.RecordingExpired ? words["expired"] : null,
            ]));
        }
    }

    private static string Line(IEnumerable<string?> cells) => string.Join(',', cells.Select(c => Field(Safe(c))));

    /// <summary>Marks a number as text for Excel.</summary>
    private const string NumberPrefix = "=\"";

    /// <summary>A cell Excel would run as a formula, with an apostrophe in front (M-S02).</summary>
    public static string? Safe(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.StartsWith(NumberPrefix, StringComparison.Ordinal) && IsNumberFormula(value))
        {
            return value;
        }

        return value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + value : value;
    }

    /// <summary>A phone number as <c>="…"</c>, so Excel keeps its leading 0 and its +.</summary>
    public static string? Number(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var number = raw.Trim();
        return number.All(c => char.IsAsciiDigit(c) || c is '+' or '-' or ' ' or '(' or ')')
            ? $"{NumberPrefix}{number}\""
            : number;
    }

    private static bool IsNumberFormula(string value) =>
        value.Length > NumberPrefix.Length && value[^1] == '"'
        && value[NumberPrefix.Length..^1].All(c => char.IsAsciiDigit(c) || c is '+' or '-' or ' ' or '(' or ')');

    /// <summary>Quoted when it holds a comma, a quote or a line break, with quotes doubled (RFC 4180).</summary>
    public static string Field(string? value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? $"\"{value.Replace("\"", "\"\"")}\"" : value;
    }
}
