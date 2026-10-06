using CallCenter.Server.Features.Reports;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Which complaints are one complaint (Dia, 6 Oct 2026): one customer, one
/// working day of 05:00 to 05:00, calls either way and applications alike.
/// </summary>
public class ComplaintCasesTests
{
    private sealed record Row(string Name, DateTimeOffset At, Guid? ContactId = null, string? Number = null);

    private static readonly Guid Yousef = Guid.NewGuid();
    private static readonly Guid Khaled = Guid.NewGuid();

    /// <summary>The restaurant's own clock on 5 October 2026, plus <paramref name="hours"/>.</summary>
    private static DateTimeOffset At(double hours)
    {
        var local = new DateTime(2026, 10, 5).AddHours(hours);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private static List<List<string>> Group(params Row[] rows) =>
        ComplaintCases.Group(rows, r => new ComplaintCases.Key(r.At, r.ContactId, r.Number))
            .Select(g => g.Select(r => r.Name).ToList())
            .ToList();

    [Fact]
    public void A_call_in_its_call_back_and_a_message_on_one_day_are_one_complaint()
    {
        Group(
                new Row("call back", At(13.5), Yousef, "970599000002"),
                new Row("call in", At(13), Yousef, "970599000002"),
                new Row("whatsapp", At(16), Yousef, "970599000002"))
            .Should().BeEquivalentTo(new[] { new[] { "call in", "call back", "whatsapp" } }, o => o.WithStrictOrdering());
    }

    [Fact]
    public void The_working_day_runs_past_midnight_to_five()
    {
        Group(
                new Row("evening", At(23.5), Yousef),
                new Row("after midnight", At(24 + 0.75), Yousef),
                new Row("half past four", At(24 + 4.5), Yousef),
                new Row("next morning", At(24 + 11), Yousef))
            .Should().BeEquivalentTo(
                new[] { new[] { "evening", "after midnight", "half past four" }, new[] { "next morning" } },
                o => o.WithStrictOrdering());

        ComplaintCases.WorkingDay(At(24 + 4.99)).Should().Be(new DateTime(2026, 10, 5));
        ComplaintCases.WorkingDay(At(24 + 5)).Should().Be(new DateTime(2026, 10, 6));
    }

    [Fact]
    public void The_same_customer_is_found_by_contact_or_by_number()
    {
        // The message has the contact; the call back to an unsaved second
        // number shares only the number with a third row that has the contact.
        Group(
                new Row("message", At(12), Yousef),
                new Row("call back", At(13), null, "970599111111"),
                new Row("second number", At(14), Yousef, "970599111111"),
                new Row("someone else", At(15), Khaled, "970599000001"))
            .Should().BeEquivalentTo(
                new[] { new[] { "message", "call back", "second number" }, new[] { "someone else" } },
                o => o.WithStrictOrdering());
    }

    [Fact]
    public void A_complaint_from_a_withheld_number_nobody_saved_is_its_own()
    {
        Group(new Row("withheld", At(12)), new Row("also withheld", At(12.5)))
            .Should().HaveCount(2);
    }
}
