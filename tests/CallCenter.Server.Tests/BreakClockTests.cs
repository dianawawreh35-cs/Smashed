using CallCenter.Server.Features.Breaks;
using CallCenter.Shared;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// When a break ended that the Agent App never ended (A-86), and how much of a
/// break falls on a day. No database: these are the rules on their own.
/// </summary>
public class BreakClockTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 15, 0, 0, TimeSpan.FromHours(3));

    private static BreakClock.Facts Open(
        DateTimeOffset started, DateTimeOffset? loggedOut = null, DateTimeOffset? lastSeen = null, bool hasSession = true) =>
        new(started, null, null, hasSession, started.AddHours(-1), loggedOut, lastSeen);

    [Fact]
    public void A_break_the_app_ended_ends_when_it_said()
    {
        var facts = new BreakClock.Facts(
            Now.AddMinutes(-20), Now.AddMinutes(-5), BreakEndings.BreakOut, true, null, null, null);

        BreakClock.EndOf(facts, Now).Should().Be(new BreakClock.Ending(Now.AddMinutes(-5), BreakEndings.BreakOut));
    }

    [Fact]
    public void An_open_break_on_a_sign_in_still_heard_from_is_still_going()
    {
        BreakClock.EndOf(Open(Now.AddMinutes(-20), lastSeen: Now.AddMinutes(-1)), Now)
            .Should().Be(BreakClock.Ending.Ongoing);
    }

    [Fact]
    public void An_open_break_ends_when_its_sign_in_ended()
    {
        BreakClock.EndOf(Open(Now.AddMinutes(-20), loggedOut: Now.AddMinutes(-3), lastSeen: Now.AddMinutes(-4)), Now)
            .Should().Be(new BreakClock.Ending(Now.AddMinutes(-3), BreakEndings.SessionEnded));
    }

    [Fact]
    public void A_laptop_that_went_silent_ends_the_break_when_it_was_last_heard_from_not_when_it_was_signed_in_again()
    {
        // Power lost at 14:10, signed in to again the next morning: ten
        // minutes of break, not eighteen hours.
        var started = Now.AddMinutes(-60);
        var facts = Open(started, loggedOut: Now.AddHours(18), lastSeen: started.AddMinutes(10));

        BreakClock.EndOf(facts, Now.AddHours(18))
            .Should().Be(new BreakClock.Ending(started.AddMinutes(10), BreakEndings.NotHeard));
    }

    [Fact]
    public void A_sign_in_still_open_but_not_heard_from_for_five_minutes_ends_the_break_at_its_last_word()
    {
        var facts = Open(Now.AddMinutes(-30), lastSeen: Now.AddMinutes(-6));

        BreakClock.EndOf(facts, Now).Should().Be(new BreakClock.Ending(Now.AddMinutes(-6), BreakEndings.NotHeard));
    }

    [Fact]
    public void A_break_begun_after_the_last_stamp_is_heard_from_at_its_start()
    {
        // The stamp is written at most once a minute; the request that began
        // the break was itself a word from the app.
        var started = Now.AddMinutes(-2);
        var facts = Open(started, lastSeen: Now.AddMinutes(-3));

        BreakClock.EndOf(facts, Now).Should().Be(BreakClock.Ending.Ongoing);
    }

    [Fact]
    public void A_break_whose_sign_in_row_is_gone_ends_where_it_began()
    {
        var started = Now.AddMinutes(-30);

        BreakClock.EndOf(Open(started, hasSession: false), Now)
            .Should().Be(new BreakClock.Ending(started, BreakEndings.NotHeard));
    }

    [Fact]
    public void A_break_over_midnight_counts_its_own_part_on_each_day()
    {
        var midnight = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(3));
        var start = midnight.AddMinutes(-20);
        var end = midnight.AddMinutes(25);

        BreakClock.SecondsWithin(start, end, midnight.AddDays(-1), midnight).Should().Be(20 * 60);
        BreakClock.SecondsWithin(start, end, midnight, midnight.AddDays(1)).Should().Be(25 * 60);
        BreakClock.SecondsWithin(start, end, midnight.AddDays(1), midnight.AddDays(2)).Should().Be(0);
    }
}
