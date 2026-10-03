using System.Net;
using System.Net.Http.Json;
using System.Text;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Breaks;
using CallCenter.Server.Features.Reports;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Contracts.Breaks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Agents' breaks against a real database (A-86, S-66, R-22): Break in and out
/// from the Agent App, resends, the break a sign-in ends, the monitor, the
/// report against the daily limit, the list and the export.
/// </summary>
/// <remarks>
/// Each test makes its own agents and reads only their rows, so other tests'
/// agents on the monitor, and rows earlier runs left, cannot leak in. The
/// report's breaks are put on yesterday, whole, so a run just after midnight
/// does not split them.
/// </remarks>
[Collection(ApiCollection.Name)]
public class BreaksTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    private static DateOnly Yesterday => DateOnly.FromDateTime(ReportScope.Local(DateTimeOffset.Now)).AddDays(-1);

    /// <summary>Noon on the restaurant's yesterday.</summary>
    private static DateTimeOffset YesterdayNoon =>
        BreaksService.Period(Yesterday, Yesterday).Start.AddHours(12);

    // ---- Break in, Break out ---------------------------------------------------

    [DatabaseFact]
    public async Task Break_in_then_break_out_saves_one_break_with_both_ends()
    {
        var (agent, client, login) = await AgentAsync();
        var id = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow.AddMinutes(-10);

        var open = await SaveAsync(client, id, new SaveBreakRequest(login.SessionId, started, null, null));
        open.EndedAt.Should().BeNull("it is still going");
        open.EndedBy.Should().BeNull();
        open.AgentId.Should().Be(agent.Id);
        open.Seconds.Should().BeInRange(9 * 60, 11 * 60);

        var ended = started.AddMinutes(7);
        var closed = await SaveAsync(client, id, new SaveBreakRequest(login.SessionId, started, ended, BreakEndings.BreakOut));

        closed.EndedAt.Should().BeCloseTo(ended, TimeSpan.FromMilliseconds(1));
        closed.EndedBy.Should().Be(BreakEndings.BreakOut);
        closed.Seconds.Should().Be(7 * 60);

        (await CountAsync(agent.Id)).Should().Be(1);
    }

    [DatabaseFact]
    public async Task A_resent_break_changes_nothing_and_an_end_that_arrives_first_is_kept()
    {
        var (agent, client, login) = await AgentAsync();
        var id = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow.AddMinutes(-30);
        var ended = started.AddMinutes(12);

        // The Break out went straight through; the Break in had been queued
        // (A-04) and arrives after it.
        await SaveAsync(client, id, new SaveBreakRequest(login.SessionId, started, ended, BreakEndings.BreakOut));
        var late = await SaveAsync(client, id, new SaveBreakRequest(login.SessionId, started, null, null));
        await SaveAsync(client, id, new SaveBreakRequest(login.SessionId, started, ended, BreakEndings.BreakOut));

        late.EndedAt.Should().BeCloseTo(ended, TimeSpan.FromMilliseconds(1), "a start that comes late does not reopen it");
        (await CountAsync(agent.Id)).Should().Be(1);
    }

    [DatabaseFact]
    public async Task A_laptop_clock_that_is_fast_does_not_put_a_break_in_the_future()
    {
        var (_, client, login) = await AgentAsync();

        var saved = await SaveAsync(client, Guid.NewGuid(),
            new SaveBreakRequest(login.SessionId, DateTimeOffset.UtcNow.AddMinutes(3), null, null));

        saved.StartedAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [DatabaseFact]
    public async Task Another_agents_break_a_break_that_ends_before_it_begins_and_one_from_last_year_are_refused()
    {
        var (_, owner, ownerLogin) = await AgentAsync();
        var (_, other, otherLogin) = await AgentAsync();
        var id = Guid.NewGuid();
        var started = DateTimeOffset.UtcNow.AddMinutes(-5);

        await SaveAsync(owner, id, new SaveBreakRequest(ownerLogin.SessionId, started, null, null));

        (await CodeAsync(other.PutAsJsonAsync($"/api/breaks/mine/{id}",
            new SaveBreakRequest(otherLogin.SessionId, started, DateTimeOffset.UtcNow, BreakEndings.BreakOut))))
            .Should().Be((HttpStatusCode.Forbidden, "not_your_break"));

        (await CodeAsync(owner.PutAsJsonAsync($"/api/breaks/mine/{Guid.NewGuid()}",
            new SaveBreakRequest(ownerLogin.SessionId, started, started.AddMinutes(-1), BreakEndings.BreakOut))))
            .Should().Be((HttpStatusCode.BadRequest, "break_ends_before_start"));

        (await CodeAsync(owner.PutAsJsonAsync($"/api/breaks/mine/{Guid.NewGuid()}",
            new SaveBreakRequest(ownerLogin.SessionId, DateTimeOffset.UtcNow.AddDays(-400), null, null))))
            .Should().Be((HttpStatusCode.BadRequest, "break_too_old"));

        (await CodeAsync(owner.PutAsJsonAsync($"/api/breaks/mine/{Guid.NewGuid()}",
            new SaveBreakRequest(ownerLogin.SessionId, started, DateTimeOffset.UtcNow, BreakEndings.NotHeard))))
            .Should().Be((HttpStatusCode.BadRequest, "bad_break_ending"), "the server's own conclusions are not the app's to send");
    }

    [DatabaseFact]
    public async Task A_new_break_ends_one_left_open()
    {
        var (agent, client, login) = await AgentAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        await SaveAsync(client, first, new SaveBreakRequest(login.SessionId, DateTimeOffset.UtcNow.AddMinutes(-20), null, null));
        var secondStart = DateTimeOffset.UtcNow.AddMinutes(-2);
        await SaveAsync(client, second, new SaveBreakRequest(login.SessionId, secondStart, null, null));

        var row = await data.QueryAsync(db => db.AgentBreaks.SingleAsync(b => b.Id == first));
        row.EndedAt.Should().BeCloseTo(secondStart, TimeSpan.FromSeconds(1));
        row.EndedBy.Should().Be(BreakEndings.BreakOut);

        (await data.QueryAsync(db => db.AgentBreaks.CountAsync(b => b.UserId == agent.Id && b.EndedAt == null)))
            .Should().Be(1, "one break at a time");
    }

    // ---- breaks a sign-in ends -------------------------------------------------

    [DatabaseFact]
    public async Task Signing_in_again_ends_a_break_left_going_on_the_last_sign_in()
    {
        var (agent, client, login) = await AgentAsync();
        var id = Guid.NewGuid();
        await SaveAsync(client, id, new SaveBreakRequest(login.SessionId, DateTimeOffset.UtcNow.AddMinutes(-4), null, null));

        // The app crashed during the break, and the agent signs in again.
        var before = DateTimeOffset.UtcNow;
        await data.SignInAsync(agent);

        var row = await data.QueryAsync(db => db.AgentBreaks.SingleAsync(b => b.Id == id));
        row.EndedBy.Should().Be(BreakEndings.SessionEnded);
        row.EndedAt.Should().BeOnOrAfter(before.AddSeconds(-1));
    }

    [DatabaseFact]
    public async Task Signing_out_ends_a_break_whose_break_out_never_arrived()
    {
        var (_, client, login) = await AgentAsync();
        var id = Guid.NewGuid();
        await SaveAsync(client, id, new SaveBreakRequest(login.SessionId, DateTimeOffset.UtcNow.AddMinutes(-4), null, null));

        (await client.PostAsJsonAsync("/api/auth/logout", new LogoutRequest(login.SessionId!.Value, LogoutReasons.Manual)))
            .IsSuccessStatusCode.Should().BeTrue();

        var row = await data.QueryAsync(db => db.AgentBreaks.SingleAsync(b => b.Id == id));
        row.EndedBy.Should().Be(BreakEndings.SessionEnded);
    }

    [DatabaseFact]
    public async Task A_break_queued_under_an_earlier_sign_in_ends_with_that_one_not_the_one_that_sent_it()
    {
        var (agent, first, firstLogin) = await AgentAsync();
        await first.GetAsync("/api/breaks/mine/today");

        // Offline at Break in, and the app restarted before the server came back.
        var (second, _) = await data.SignInAsync(agent);
        var id = Guid.NewGuid();
        var saved = await SaveAsync(second, id,
            new SaveBreakRequest(firstLogin.SessionId, DateTimeOffset.UtcNow.AddMinutes(-3), null, null));

        saved.EndedAt.Should().NotBeNull("the sign-in it was taken under is over");
        saved.EndedBy.Should().BeOneOf(BreakEndings.SessionEnded, BreakEndings.NotHeard);
        (await data.QueryAsync(db => db.AgentBreaks.SingleAsync(b => b.Id == id))).SessionId.Should().Be(firstLogin.SessionId);
    }

    // ---- the agent's own total -------------------------------------------------

    [DatabaseFact]
    public async Task Today_adds_up_every_break_today_and_gives_the_limit()
    {
        var (_, client, login) = await AgentAsync();
        var (todayStart, _) = ReportScope.Today();
        var now = DateTimeOffset.UtcNow;

        // Two closed breaks inside today, whatever the hour.
        var a = Max(todayStart, now.AddMinutes(-30));
        await SaveAsync(client, Guid.NewGuid(), new SaveBreakRequest(login.SessionId, a, Min(a.AddMinutes(5), now), BreakEndings.BreakOut));
        var b = Max(todayStart, now.AddMinutes(-10));
        await SaveAsync(client, Guid.NewGuid(), new SaveBreakRequest(login.SessionId, b, Min(b.AddMinutes(3), now), BreakEndings.BreakOut));

        var today = (await client.GetFromJsonAsync<MyBreaksTodayDto>("/api/breaks/mine/today"))!;

        var expected = (int)((Min(a.AddMinutes(5), now) - a) + (Min(b.AddMinutes(3), now) - b)).TotalSeconds;
        today.Seconds.Should().BeCloseTo(expected, 2);
        today.DailyLimitMinutes.Should().BePositive();
    }

    // ---- the monitor (S-66) ------------------------------------------------------

    [DatabaseFact]
    public async Task The_monitor_shows_who_is_on_break_since_when_who_is_working_and_who_is_away()
    {
        var (onBreak, onBreakClient, onBreakLogin) = await AgentAsync();
        var (working, _, _) = await AgentAsync();
        var away = await data.CreateUserAsync();
        var silent = await data.CreateUserAsync();
        var (_, silentLogin) = await data.SignInAsync(silent);
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var started = DateTimeOffset.UtcNow.AddMinutes(-3);
        await SaveAsync(onBreakClient, Guid.NewGuid(), new SaveBreakRequest(onBreakLogin.SessionId, started, null, null));

        await data.QueryAsync(db => db.AgentSessions.Where(s => s.Id == silentLogin.SessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, DateTimeOffset.UtcNow.AddMinutes(-20))));

        var monitor = (await supervisor.GetFromJsonAsync<BreakMonitorDto>("/api/breaks/monitor"))!;
        var rows = monitor.Agents.ToDictionary(r => r.AgentId);

        rows[onBreak.Id].State.Should().Be(BreakStates.OnBreak);
        rows[onBreak.Id].BreakStartedAt.Should().BeCloseTo(started, TimeSpan.FromMilliseconds(1));
        rows[onBreak.Id].TodayBreaks.Should().Be(1);
        rows[working.Id].State.Should().Be(BreakStates.Working);
        rows[working.Id].TodaySeconds.Should().Be(0);
        rows[away.Id].State.Should().Be(BreakStates.SignedOut);
        rows[silent.Id].State.Should().Be(BreakStates.NotHeard);
        monitor.DailyLimitMinutes.Should().BePositive();
    }

    [DatabaseFact]
    public async Task The_monitor_shows_do_not_disturb_as_the_app_last_said_it()
    {
        var (agent, client, login) = await AgentAsync();
        var (quiet, _, _) = await AgentAsync();
        var silent = await data.CreateUserAsync();
        var (silentClient, silentLogin) = await data.SignInAsync(silent);
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        async Task<BreakMonitorRowDto> RowAsync(Guid id) =>
            (await supervisor.GetFromJsonAsync<BreakMonitorDto>("/api/breaks/monitor"))!.Agents.Single(r => r.AgentId == id);

        var now = DateTimeOffset.UtcNow;
        var signedIn = now.AddHours(-1);
        await data.QueryAsync(db => db.AgentSessions.Where(s => s.Id == login.SessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LoggedInAt, signedIn)));

        // Left on from the last shift: on since this sign-in, not since then.
        await SwitchAsync(client, new SaveDoNotDisturbRequest(true, now.AddHours(-5)));
        var on = await RowAsync(agent.Id);
        on.DoNotDisturb.Should().BeTrue();
        on.DoNotDisturbSince.Should().BeCloseTo(signedIn, TimeSpan.FromMilliseconds(1));

        // The same again keeps the first "since".
        await SwitchAsync(client, new SaveDoNotDisturbRequest(true, now));
        (await RowAsync(agent.Id)).Should().BeEquivalentTo(on);

        // Off, then news older than that arriving late changes nothing.
        var offAt = now.AddMinutes(-10);
        await SwitchAsync(client, new SaveDoNotDisturbRequest(false, offAt));
        await SwitchAsync(client, new SaveDoNotDisturbRequest(true, now.AddMinutes(-20)));
        var off = await RowAsync(agent.Id);
        off.DoNotDisturb.Should().BeFalse();
        off.DoNotDisturbSince.Should().BeCloseTo(offAt, TimeSpan.FromMilliseconds(1));

        // An app that never said, and one not heard from, show nothing.
        (await RowAsync(quiet.Id)).DoNotDisturb.Should().BeNull();

        await SwitchAsync(silentClient, new SaveDoNotDisturbRequest(true, DateTimeOffset.UtcNow));
        await data.QueryAsync(db => db.AgentSessions.Where(s => s.Id == silentLogin.SessionId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastSeenAt, DateTimeOffset.UtcNow.AddMinutes(-20))));
        var notHeard = await RowAsync(silent.Id);
        notHeard.State.Should().Be(BreakStates.NotHeard);
        notHeard.DoNotDisturb.Should().BeNull();
        notHeard.DoNotDisturbSince.Should().BeNull();
    }

    // ---- the report and the list (R-22) -----------------------------------------

    [DatabaseFact]
    public async Task The_report_counts_each_day_against_the_limit()
    {
        var (agent, client, login) = await AgentAsync();
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var limit = (await supervisor.GetFromJsonAsync<BreakMonitorDto>("/api/breaks/monitor"))!.DailyLimitMinutes;

        // Two breaks yesterday that together run ten minutes past the limit.
        var first = YesterdayNoon;
        await SaveAsync(client, Guid.NewGuid(), new SaveBreakRequest(login.SessionId, first, first.AddMinutes(limit), BreakEndings.BreakOut));
        var second = first.AddHours(2);
        await SaveAsync(client, Guid.NewGuid(), new SaveBreakRequest(login.SessionId, second, second.AddMinutes(10), BreakEndings.SignedOut));

        var report = (await supervisor.GetFromJsonAsync<BreakReportDto>(
            $"/api/breaks/report?from={Yesterday:yyyy-MM-dd}&to={Yesterday:yyyy-MM-dd}&agentId={agent.Id}"))!;

        var day = report.Days.Should().ContainSingle().Subject;
        day.Day.Should().Be(Yesterday);
        day.Breaks.Should().Be(2);
        day.Seconds.Should().Be((limit + 10) * 60);
        day.OverSeconds.Should().Be(10 * 60);

        var total = report.Agents.Should().ContainSingle().Subject;
        total.AgentId.Should().Be(agent.Id);
        total.Days.Should().Be(1);
        total.DaysOver.Should().Be(1);
        total.OverSeconds.Should().Be(10 * 60);
    }

    [DatabaseFact]
    public async Task A_break_over_midnight_is_split_between_the_two_days()
    {
        var (agent, client, login) = await AgentAsync();
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var midnight = BreaksService.Period(Yesterday, Yesterday).Start;
        await SaveAsync(client, Guid.NewGuid(),
            new SaveBreakRequest(login.SessionId, midnight.AddMinutes(-15), midnight.AddMinutes(20), BreakEndings.BreakOut));

        var before = Yesterday.AddDays(-1);
        var report = (await supervisor.GetFromJsonAsync<BreakReportDto>(
            $"/api/breaks/report?from={before:yyyy-MM-dd}&to={Yesterday:yyyy-MM-dd}&agentId={agent.Id}"))!;

        report.Days.Should().HaveCount(2);
        report.Days.Single(d => d.Day == before).Seconds.Should().Be(15 * 60);
        report.Days.Single(d => d.Day == Yesterday).Seconds.Should().Be(20 * 60);
        report.Agents.Single().Breaks.Should().Be(1, "it is one break, on two days");
    }

    [DatabaseFact]
    public async Task The_list_pages_newest_first_and_the_export_holds_every_break()
    {
        var (agent, client, login) = await AgentAsync();
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        for (var i = 0; i < 3; i++)
        {
            var start = YesterdayNoon.AddHours(i);
            await SaveAsync(client, Guid.NewGuid(), new SaveBreakRequest(login.SessionId, start, start.AddMinutes(5 + i), BreakEndings.BreakOut));
        }

        var query = $"from={Yesterday:yyyy-MM-dd}&to={Yesterday:yyyy-MM-dd}&agentId={agent.Id}";
        var page = (await supervisor.GetFromJsonAsync<BreakPageDto>($"/api/breaks?{query}&page=1&pageSize=2"))!;

        page.Total.Should().Be(3);
        page.Rows.Should().HaveCount(2);
        page.Rows[0].StartedAt.Should().BeAfter(page.Rows[1].StartedAt, "newest first");
        page.Rows[0].Seconds.Should().Be(7 * 60);

        var csv = Encoding.UTF8.GetString(await supervisor.GetByteArrayAsync($"/api/breaks/export?{query}&lang=en"));
        csv.Should().StartWith("﻿Agent,Day,Break in,Break out,Minutes,Ended by");
        csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Should().HaveCount(4, "a heading and every break, not the page");
        csv.Should().Contain(agent.DisplayName).And.Contain("7.0").And.Contain("Break out");
    }

    [DatabaseFact]
    public async Task The_allowance_is_on_the_settings_page_with_a_value_not_an_empty_box()
    {
        // Written by the migration SeedBreakAllowance, as an update does not
        // run the seed (Dia, 1 Oct).
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var settings = (await supervisor.GetFromJsonAsync<List<CallCenter.Shared.Contracts.Settings.SettingDto>>("/api/settings"))!;
        var allowance = settings.Should().ContainSingle(s => s.Key == BreaksService.DailyLimitKey).Subject;

        int.TryParse(allowance.Value, out var minutes).Should().BeTrue($"the box should hold a number, not '{allowance.Value}'");
        minutes.Should().BePositive();
    }

    [DatabaseFact]
    public async Task A_period_that_runs_backwards_is_refused()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        (await CodeAsync(supervisor.GetAsync($"/api/breaks/report?from={Yesterday:yyyy-MM-dd}&to={Yesterday.AddDays(-1):yyyy-MM-dd}")))
            .Should().Be((HttpStatusCode.BadRequest, "bad_period"));
    }

    [DatabaseFact]
    public async Task Agents_cannot_read_the_monitor_and_supervisors_take_no_breaks()
    {
        var (_, agent, _) = await AgentAsync();
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        (await agent.GetAsync("/api/breaks/monitor")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.GetAsync("/api/breaks/report")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await supervisor.PutAsJsonAsync($"/api/breaks/mine/{Guid.NewGuid()}",
            new SaveBreakRequest(null, DateTimeOffset.UtcNow, null, null))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- helpers -------------------------------------------------------------------

    private async Task<(User Agent, HttpClient Client, LoginResponse Login)> AgentAsync()
    {
        var agent = await data.CreateUserAsync();
        var (client, login) = await data.SignInAsync(agent);
        return (agent, client, login);
    }

    private static async Task<BreakDto> SaveAsync(HttpClient client, Guid id, SaveBreakRequest request)
    {
        var response = await client.PutAsJsonAsync($"/api/breaks/mine/{id}", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BreakDto>())!;
    }

    private static async Task SwitchAsync(HttpClient client, SaveDoNotDisturbRequest request)
    {
        var response = await client.PutAsJsonAsync("/api/breaks/mine/do-not-disturb", request);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
    }

    private Task<int> CountAsync(Guid agentId) =>
        data.QueryAsync(db => db.AgentBreaks.CountAsync(b => b.UserId == agentId));

    private static async Task<(HttpStatusCode, string?)> CodeAsync(Task<HttpResponseMessage> sending)
    {
        var response = await sending;
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return (response.StatusCode, body.TryGetProperty("code", out var code) ? code.GetString() : null);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
