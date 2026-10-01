using System.Globalization;
using System.Text;
using CallCenter.Server.Features.Communications;
using CallCenter.Server.Features.Reports;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Breaks;

namespace CallCenter.Server.Features.Breaks;

/// <summary>
/// The single breaks of a period as a file Excel opens (R-22, S-05): every one,
/// not the page on screen.
/// </summary>
/// <remarks>
/// CSV with a byte-order mark, as every export here is (Dia chose CSV over
/// <c>.xlsx</c>, 26 Sep), and written by <see cref="CallExport"/>'s rules, so
/// no cell runs as a formula (M-S02). Times in the restaurant's own; the length
/// in minutes, to one decimal, which is what a spreadsheet sums.
/// </remarks>
public static class BreakExport
{
    private static readonly Dictionary<string, string[]> Headings = new()
    {
        ["en"] = ["Agent", "Day", "Break in", "Break out", "Minutes", "Ended by"],
        ["ar"] = ["الموظف", "اليوم", "بداية الاستراحة", "نهاية الاستراحة", "الدقائق", "انتهت بـ"],
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Words = new()
    {
        ["en"] = new()
        {
            [BreakEndings.BreakOut] = "Break out",
            [BreakEndings.SignedOut] = "Signed out",
            [BreakEndings.SessionEnded] = "Sign-in ended",
            [BreakEndings.NotHeard] = "App stopped",
            [""] = "Still on break",
        },
        ["ar"] = new()
        {
            [BreakEndings.BreakOut] = "إنهاء الاستراحة",
            [BreakEndings.SignedOut] = "تسجيل الخروج",
            [BreakEndings.SessionEnded] = "انتهاء الدخول",
            [BreakEndings.NotHeard] = "توقف البرنامج",
            [""] = "ما زال في استراحة",
        },
    };

    public static async Task WriteAsync(
        Stream output, IEnumerable<BreakDto> rows, string? lang, CancellationToken ct)
    {
        var language = CallExport.Language(lang);
        var words = Words[language];

        // The byte-order mark, or Excel reads the Arabic as Windows-1252.
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true);
        writer.NewLine = "\r\n";

        await writer.WriteLineAsync(Line(Headings[language]));

        foreach (var b in rows)
        {
            ct.ThrowIfCancellationRequested();
            var started = ReportScope.Local(b.StartedAt);

            await writer.WriteLineAsync(Line(
            [
                b.AgentDisplayName,
                started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                started.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
                b.EndedAt is { } ended ? ReportScope.Local(ended).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) : null,
                (b.Seconds / 60.0).ToString("0.0", CultureInfo.InvariantCulture),
                words.TryGetValue(b.EndedBy ?? string.Empty, out var word) ? word : b.EndedBy,
            ]));
        }
    }

    private static string Line(IEnumerable<string?> cells) =>
        string.Join(',', cells.Select(c => CallExport.Field(CallExport.Safe(c))));
}
