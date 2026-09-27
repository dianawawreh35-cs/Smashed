using System.Text;
using CallCenter.Server.Features.AgentLogs;
using CallCenter.Shared.Contracts.AgentLogs;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>An Agent App's log read as entries, for the Logs page (N-12).</summary>
public class AgentLogReaderTests
{
    // As the app writes it: an exception runs on under its entry, and so does a SQL command.
    private const string Day =
        "2026-09-27 09:00:00.000 +03:00 [INF] Agent App started. Logs: C:\\logs\n" +
        "2026-09-27 09:00:05.120 +03:00 [WRN] Extension 2001 not registered yet: TimedOut.\n" +
        "2026-09-27 09:01:00.300 +03:00 [INF] Executed DbCommand (0ms)\n" +
        "SELECT \"p\".\"Id\"\n" +
        "FROM \"pending_uploads\" AS \"p\"\n" +
        "2026-09-27 09:02:00.000 +03:00 [ERR] The call could not be logged\n" +
        "System.Net.Http.HttpRequestException: No route to host\n" +
        "   at CallCenter.AgentApp.Services.ApiClient.SendAsync()\n" +
        "2026-09-27 09:03:00.000 +03:00 [FTL] The app stopped\n";

    [Fact]
    public void An_entry_carries_the_lines_under_it()
    {
        var entries = AgentLogReader.Entries(Day);

        entries.Select(e => e.Level).Should().Equal("INF", "WRN", "INF", "ERR", "FTL");

        var error = entries[3];
        error.Line.Should().Be(6);
        error.Time.Should().Be("2026-09-27 09:02:00.000 +03:00");
        error.Text.Should().Be(
            "The call could not be logged\n" +
            "System.Net.Http.HttpRequestException: No route to host\n" +
            "   at CallCenter.AgentApp.Services.ApiClient.SendAsync()");
    }

    [Fact]
    public void Lines_before_the_first_entry_are_kept_without_a_level()
    {
        var entries = AgentLogReader.Entries("   at Something.Cut()\n2026-09-27 09:00:00.000 +03:00 [INF] Started\n");

        entries.Should().HaveCount(2);
        entries[0].Should().Be(new AgentLogEntryDto(1, "", "", "   at Something.Cut()"));
        entries[1].Line.Should().Be(2);
    }

    [Fact]
    public void A_page_is_newest_first_and_counts_the_whole_day()
    {
        var page = AgentLogReader.Page(Day, AgentLogLevels.All, null);

        page.Entries.Select(e => e.Level).Should().Equal("FTL", "ERR", "INF", "WRN", "INF");
        page.Should().BeEquivalentTo(new { Matched = 5, Total = 5, Errors = 2, Warnings = 1 });
    }

    [Fact]
    public void Errors_only_takes_fatal_ones_too_and_warnings_takes_both()
    {
        AgentLogReader.Page(Day, AgentLogLevels.Errors, null).Entries
            .Select(e => e.Level).Should().Equal("FTL", "ERR");

        AgentLogReader.Page(Day, AgentLogLevels.Warnings, null).Entries
            .Select(e => e.Level).Should().Equal("FTL", "ERR", "WRN");
    }

    [Fact]
    public void Search_looks_inside_the_lines_under_an_entry_too()
    {
        var page = AgentLogReader.Page(Day, AgentLogLevels.All, "no ROUTE");

        page.Entries.Should().ContainSingle().Which.Level.Should().Be("ERR");
        page.Matched.Should().Be(1);
        page.Total.Should().Be(5, "the day is still five entries");
    }

    [Fact]
    public void The_byte_count_agrees_with_the_entries()
    {
        AgentLogReader.Count(Encoding.UTF8.GetBytes(Day)).Should().Be((2, 1));
    }

    [Fact]
    public void Counting_in_pieces_adds_up_to_counting_the_whole()
    {
        var bytes = Encoding.UTF8.GetBytes(Day);
        var cut = Day.IndexOf("2026-09-27 09:02", StringComparison.Ordinal);

        var first = AgentLogReader.Count(bytes.AsSpan(0, cut));
        var rest = AgentLogReader.Count(bytes.AsSpan(cut));

        (first.Errors + rest.Errors, first.Warnings + rest.Warnings).Should().Be((2, 1));
    }
}
