using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Communications;
using CallCenter.Server.Features.Settings;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// A-42's edge: a call taken at 23:55 belongs to the day it was taken, so the
/// agent may still change it at 23:59 and not at 00:05.
/// </summary>
/// <remarks>
/// The window read <c>DateTimeOffset.Now</c> until 27 Sep (M-S08), so this could
/// not be tested. It reads the settings table for the window, hence the database.
/// </remarks>
[Collection(ApiCollection.Name)]
public class CallEditWindowTests(CallCenterApiFactory factory)
{
    private static readonly Guid Agent = Guid.NewGuid();

    [DatabaseFact]
    public async Task A_call_at_23_55_is_the_agent_s_to_change_until_midnight_and_not_after()
    {
        var call = new Communication { AgentId = Agent, StartedAt = Local(2026, 9, 27, 23, 55) };

        (await CheckAsync(call, Local(2026, 9, 27, 23, 59))).Should().BeNull("still the day of the call");
        (await CheckAsync(call, Local(2026, 9, 28, 0, 5))).Should().Be(CallEditWindow.Refusal.Closed, "the shift's day is over");
    }

    [DatabaseFact]
    public async Task A_call_at_00_05_is_today_s_although_it_is_still_yesterday_in_UTC()
    {
        var call = new Communication { AgentId = Agent, StartedAt = Local(2026, 9, 28, 0, 5) };

        (await CheckAsync(call, Local(2026, 9, 28, 9, 0))).Should().BeNull();
    }

    private async Task<CallEditWindow.Refusal?> CheckAsync(Communication call, DateTimeOffset now)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();

        // SameDay is the default; a row another test left saying Always would
        // make this pass for the wrong reason.
        (await settings.GetStringAsync("agent.edit_window", "SameDay")).Should().Be("SameDay");

        return await new CallEditWindow(settings, new FixedClock(now)).CheckAsync(call, Agent, actorIsSupervisor: false);
    }

    /// <summary>A wall-clock time in the restaurant's zone.</summary>
    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
    {
        var local = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local));
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
    }
}
