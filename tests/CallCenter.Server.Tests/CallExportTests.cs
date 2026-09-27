using CallCenter.Server.Features.Communications;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// What the R-02 export writes into a cell (M-S02): nothing Excel runs as a
/// formula, and phone numbers that come out exactly as they went in.
/// </summary>
public class CallExportTests
{
    [Theory]
    [InlineData("=HYPERLINK(\"http://x\",\"click\")")]
    [InlineData("+1+2")]
    [InlineData("-5")]
    [InlineData("@SUM(A1)")]
    [InlineData("\tindented")]
    [InlineData("\rreturn")]
    public void A_cell_that_would_run_as_a_formula_gets_an_apostrophe(string value)
    {
        CallExport.Safe(value).Should().Be("'" + value);
    }

    [Theory]
    [InlineData("سارة الحلبي")]
    [InlineData("cold burger - again")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_left_alone(string? value)
    {
        CallExport.Safe(value).Should().Be(value);
    }

    [Theory]
    [InlineData("0599123456", "=\"0599123456\"")]
    [InlineData("+970599123456", "=\"+970599123456\"")]
    [InlineData(" 059 912 3456 ", "=\"059 912 3456\"")]
    [InlineData("2001", "=\"2001\"")]
    public void A_phone_number_is_written_so_Excel_keeps_it_as_typed(string raw, string cell)
    {
        // As plain text Excel dropped the 0 of 0599… and read +970… as a sum.
        CallExport.Number(raw).Should().Be(cell);
        CallExport.Safe(CallExport.Number(raw)).Should().Be(cell, "the number form is not itself neutralised");
    }

    [Fact]
    public void A_number_column_holding_something_else_is_treated_like_any_cell()
    {
        var odd = "=cmd|' /C calc'!A0";

        CallExport.Number(odd).Should().Be(odd);
        CallExport.Safe(CallExport.Number(odd)).Should().Be("'" + odd);
        CallExport.Safe("=\"0599\"&cmd").Should().StartWith("'", "only a formula holding nothing but a number passes");
    }

    [Fact]
    public void The_number_form_survives_CSV_quoting()
    {
        CallExport.Field(CallExport.Number("0599123456")).Should().Be("\"=\"\"0599123456\"\"\"");
    }
}
