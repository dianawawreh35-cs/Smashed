using System.Globalization;
using System.Text;
using CallCenter.Server.Features.Communications;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Mistakes;

namespace CallCenter.Server.Features.Mistakes;

/// <summary>
/// The mistakes as a file Excel opens (S-65, S-05): every match of the page's
/// filters, not the page on screen.
/// </summary>
/// <remarks>
/// CSV with a byte-order mark, as every export here is (Dia chose CSV over
/// <c>.xlsx</c>, 26 Sep), and written by <see cref="CallExport"/>'s rules: no
/// cell runs as a formula (M-S02) and a phone number keeps its leading 0.
/// Headings in the supervisor's language.
/// </remarks>
public static class MistakeExport
{
    private static readonly Dictionary<string, string[]> Headings = new()
    {
        ["en"] = ["Date", "Branch", "Responsible", "Agent", "Customer", "Number", "Value", "Notes", "Recorded by"],
        ["ar"] = ["التاريخ", "الفرع", "المسؤول", "الموظف", "الزبون", "الرقم", "القيمة", "ملاحظات", "سجّله"],
    };

    private static readonly Dictionary<string, Dictionary<string, string>> Words = new()
    {
        ["en"] = new() { [MistakeResponsibilities.Branch] = "Branch", [MistakeResponsibilities.Agent] = "Agent" },
        ["ar"] = new() { [MistakeResponsibilities.Branch] = "الفرع", [MistakeResponsibilities.Agent] = "موظف" },
    };

    public static async Task WriteAsync(
        Stream output, IAsyncEnumerable<MistakeDto> rows, string? lang, CancellationToken ct)
    {
        var language = CallExport.Language(lang);
        var words = Words[language];

        // The byte-order mark, or Excel reads the Arabic as Windows-1252.
        await using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true);
        writer.NewLine = "\r\n";

        await writer.WriteLineAsync(Line(Headings[language]));

        await foreach (var m in rows.WithCancellation(ct))
        {
            await writer.WriteLineAsync(Line(
            [
                m.OccurredOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                m.BranchName,
                words.GetValueOrDefault(m.Responsible, m.Responsible),
                m.AgentDisplayName,
                m.ContactName,
                CallExport.Number(m.CustomerNumber),
                m.Value?.ToString(CultureInfo.InvariantCulture),
                m.Notes,
                m.CreatedByDisplayName,
            ]));
        }
    }

    private static string Line(IEnumerable<string?> cells) =>
        string.Join(',', cells.Select(c => CallExport.Field(CallExport.Safe(c))));
}
