using System.Net;
using System.Net.Http.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Features.Pbx;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Pbx;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Opening and closing the queue (S-60): <c>*280</c> is a toggle, so it is
/// dialled only from a known state and only when it changes something.
/// </summary>
/// <remarks>
/// No PBX here: the dialer only takes notes. The queue's state is one set of
/// settings for the whole database, so each test starts by clearing it and
/// ends by clearing it again.
/// </remarks>
[Collection(ApiCollection.Name)]
public class PbxQueueTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task The_queue_is_not_switched_until_someone_has_said_which_state_it_is_in()
    {
        var (client, login) = await SupervisorAsync();
        var dialer = new NoteTakingDialer();

        await ResetAsync(client);
        try
        {
            (await client.GetFromJsonAsync<QueueStatusDto>("/api/pbx/queue"))!.IsOpen.Should().BeNull();

            var result = await SwitchAsync(dialer, open: true, login.User.Id);

            result.Ok.Should().BeFalse();
            result.Code.Should().Be(PbxQueueSwitch.Codes.UnknownState);
            dialer.Codes.Should().BeEmpty("a toggle from an unknown state could close the queue");
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task Opening_a_closed_queue_dials_280_once_and_opening_it_again_dials_nothing()
    {
        var (client, login) = await SupervisorAsync();
        var dialer = new NoteTakingDialer();

        await ResetAsync(client);
        try
        {
            await MarkAsync(client, open: false);

            var opened = await SwitchAsync(dialer, open: true, login.User.Id);
            var again = await SwitchAsync(dialer, open: true, login.User.Id);

            opened.Ok.Should().BeTrue();
            opened.Status.IsOpen.Should().BeTrue();
            opened.Status.ChangedBy.Should().Be(login.User.DisplayName);
            again.Ok.Should().BeTrue();
            dialer.Codes.Should().Equal(["*280"], "the queue was already open the second time");

            var closed = await SwitchAsync(dialer, open: false, login.User.Id);
            closed.Status.IsOpen.Should().BeFalse();
            dialer.Codes.Should().Equal("*280", "*280");

            var actions = await data.QueryAsync(db => db.AuditLog
                .Where(e => e.Entity == "queue" && e.UserId == login.User.Id)
                .OrderBy(e => e.Id)
                .Select(e => e.Action)
                .ToListAsync());
            actions.Should().Equal("mark_closed", "open", "close");
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task A_tab_closed_after_the_PBX_answered_still_leaves_the_new_state_written_down()
    {
        // F-14: the PBX has toggled the queue by the time the supervisor's
        // request is cancelled. Unsaved, the next press would flip it the
        // wrong way.
        var (client, login) = await SupervisorAsync();
        using var tab = new CancellationTokenSource();
        var dialer = new DialerWhoseCallerLeaves(tab);

        await ResetAsync(client);
        try
        {
            await MarkAsync(client, open: false);

            var result = await WithQueueAsync(dialer, TimeProvider.System, q => q.SwitchAsync(true, login.User.Id, tab.Token));

            tab.IsCancellationRequested.Should().BeTrue("the request had gone by the time the PBX finished");
            result.Ok.Should().BeTrue();
            var status = await client.GetFromJsonAsync<QueueStatusDto>("/api/pbx/queue");
            status!.IsOpen.Should().BeTrue("the PBX opened it, and the server says so");
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task A_failed_call_leaves_the_state_as_it_was_and_says_why()
    {
        var (client, login) = await SupervisorAsync();

        await ResetAsync(client);
        try
        {
            await MarkAsync(client, open: true);

            var result = await SwitchAsync(new NoteTakingDialer(failWith: "The PBX did not answer *280."), open: false, login.User.Id);

            result.Ok.Should().BeFalse();
            result.Code.Should().Be(PbxQueueSwitch.Codes.PbxFailed);
            result.Error.Should().Contain("did not answer");
            result.Status.IsOpen.Should().BeTrue();
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task Without_the_server_extension_the_switch_says_so()
    {
        var (client, login) = await SupervisorAsync();
        var dialer = new NoteTakingDialer();

        await ResetAsync(client);
        await CleanUpAsync();
        await MarkAsync(client, open: false);
        try
        {
            var result = await SwitchAsync(dialer, open: true, login.User.Id);

            result.Code.Should().Be(PbxQueueSwitch.Codes.NotConfigured);
            result.Status.Configured.Should().BeFalse();
            dialer.Codes.Should().BeEmpty();
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task Agents_cannot_see_or_switch_the_queue()
    {
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());

        (await agent.GetAsync("/api/pbx/queue")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.PostAsJsonAsync("/api/pbx/queue/switch", new SetQueueRequest(true)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.PutAsJsonAsync("/api/pbx/queue/state", new SetQueueRequest(true)))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- the daily opening ------------------------------------------------------

    [DatabaseFact]
    public async Task The_queue_opens_by_itself_at_seven_once_a_day()
    {
        var (client, login) = await SupervisorAsync();
        var dialer = new NoteTakingDialer();
        var clock = new LocalClock(6, 0);

        await ResetAsync(client);
        try
        {
            await SetAutoOpenAsync("07:00");
            await WithQueueAsync(dialer, clock, q => q.MarkAsync(false, login.User.Id));

            clock.Set(6, 59);
            (await AutoOpenAsync(dialer, clock)).Should().BeFalse("it is not seven yet");

            clock.Set(7, 0);
            (await AutoOpenAsync(dialer, clock)).Should().BeTrue();
            clock.Set(7, 1);
            (await AutoOpenAsync(dialer, clock)).Should().BeFalse("the day's opening is done");

            dialer.Codes.Should().Equal("*280");

            var status = await WithQueueAsync(dialer, clock, q => q.StatusAsync());
            status.IsOpen.Should().BeTrue();
            status.ChangedAutomatically.Should().BeTrue();
            status.ChangedBy.Should().BeNull();
            status.AutoOpenAt.Should().Be("07:00");
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task A_supervisor_who_switched_the_queue_after_seven_is_not_overruled()
    {
        var (client, login) = await SupervisorAsync();
        var dialer = new NoteTakingDialer();
        var clock = new LocalClock(7, 10);

        await ResetAsync(client);
        try
        {
            await SetAutoOpenAsync("07:00");

            // A supervisor has just said the queue is closed, after seven.
            // That is the day's decision.
            await WithQueueAsync(dialer, clock, q => q.MarkAsync(false, login.User.Id));

            clock.Set(7, 11);
            (await AutoOpenAsync(dialer, clock)).Should().BeFalse();
            dialer.Codes.Should().BeEmpty();
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task A_server_started_long_after_seven_does_not_open_the_queue_late()
    {
        var (client, login) = await SupervisorAsync();
        var dialer = new NoteTakingDialer();
        var clock = new LocalClock(6, 0);

        await ResetAsync(client);
        try
        {
            await SetAutoOpenAsync("07:00");
            await WithQueueAsync(dialer, clock, q => q.MarkAsync(false, login.User.Id));

            clock.Set(8, 30);
            (await AutoOpenAsync(dialer, clock)).Should().BeFalse();

            dialer.Codes.Should().BeEmpty();
            (await WithQueueAsync(dialer, clock, q => q.StatusAsync())).AutoOpenProblem
                .Should().Contain("not running");
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task An_unknown_state_is_not_opened_and_the_card_says_why()
    {
        var (client, _) = await SupervisorAsync();
        var dialer = new NoteTakingDialer();
        var clock = new LocalClock(7, 0);

        await ResetAsync(client);
        try
        {
            await SetAutoOpenAsync("07:00");

            await AutoOpenAsync(dialer, clock);

            dialer.Codes.Should().BeEmpty();
            (await WithQueueAsync(dialer, clock, q => q.StatusAsync())).AutoOpenProblem
                .Should().Contain("open or closed right now");
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task A_call_the_PBX_never_answered_is_tried_again_and_an_answered_one_is_not()
    {
        var (client, login) = await SupervisorAsync();
        var clock = new LocalClock(6, 0);

        await ResetAsync(client);
        try
        {
            await SetAutoOpenAsync("07:00");
            await WithQueueAsync(new NoteTakingDialer(), clock, q => q.MarkAsync(false, login.User.Id));

            var unanswered = new NoteTakingDialer(failWith: "The PBX did not answer *280.", answered: false);
            clock.Set(7, 0);
            await AutoOpenAsync(unanswered, clock);
            clock.Set(7, 2);
            await AutoOpenAsync(unanswered, clock);
            unanswered.Codes.Should().HaveCount(1, "the retry waits five minutes");

            // Answered, then no sound: the queue may have opened. A second
            // *280 could close it again, so the day's opening stops here.
            var answered = new NoteTakingDialer(failWith: "No sound reached the server.", answered: true);
            clock.Set(7, 5);
            await AutoOpenAsync(answered, clock);
            clock.Set(7, 20);
            await AutoOpenAsync(answered, clock);
            answered.Codes.Should().HaveCount(1);

            var status = await WithQueueAsync(answered, clock, q => q.StatusAsync());
            status.IsOpen.Should().BeFalse("nothing is known to have changed");
            status.AutoOpenProblem.Should().Contain("No sound");
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    // ---- helpers ----------------------------------------------------------------

    private async Task<(HttpClient Client, Shared.Contracts.Auth.LoginResponse Login)> SupervisorAsync() =>
        await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

    /// <summary>No known state, and the server's extension set up.</summary>
    private async Task ResetAsync(HttpClient client)
    {
        await CleanUpAsync();
        (await client.PutAsJsonAsync("/api/pbx/blacklist", new UpdatePbxBlacklistRequest("7999", "secret-for-tests")))
            .EnsureSuccessStatusCode();
    }

    private static async Task MarkAsync(HttpClient client, bool open) =>
        (await client.PutAsJsonAsync("/api/pbx/queue/state", new SetQueueRequest(open))).EnsureSuccessStatusCode();

    /// <summary>One press of Open or Close with <paramref name="dialer"/>, on the host with a PBX address.</summary>
    private Task<QueueSwitchResultDto> SwitchAsync(IPbxFeatureDialer dialer, bool open, Guid userId) =>
        WithQueueAsync(dialer, TimeProvider.System, q => q.SwitchAsync(open, userId));

    private Task<bool> AutoOpenAsync(IPbxFeatureDialer dialer, TimeProvider clock) =>
        WithQueueAsync(dialer, clock, q => q.AutoOpenIfDueAsync());

    /// <summary>Runs <paramref name="use"/> on a queue switch of its own, with this dialer and clock.</summary>
    private async Task<T> WithQueueAsync<T>(IPbxFeatureDialer dialer, TimeProvider clock, Func<PbxQueueSwitch, Task<T>> use)
    {
        await using var scope = factory.RealAccounts.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var queue = new PbxQueueSwitch(
            services.GetRequiredService<CallCenterDbContext>(),
            dialer,
            services.GetRequiredService<PbxFeatureLine>(),
            new PbxQueueGate(),
            clock,
            NullLogger<PbxQueueSwitch>.Instance);

        return await use(queue);
    }

    private Task SetAutoOpenAsync(string time) =>
        data.QueryAsync(async db =>
        {
            await db.Settings.Where(s => s.Key == PbxQueueSwitch.AutoOpenTimeKey).ExecuteDeleteAsync();
            db.Settings.Add(new Data.Entities.Setting
            {
                Key = PbxQueueSwitch.AutoOpenTimeKey,
                Value = time,
                UpdatedAt = DateTimeOffset.UtcNow,
            });
            return await db.SaveChangesAsync();
        });

    private Task CleanUpAsync() =>
        data.QueryAsync(db => db.Settings
            .Where(s => s.Key.StartsWith(PbxQueueSwitch.Keys.Prefix) || s.Key.StartsWith(PbxFeatureLine.Keys.Prefix)
                || s.Key == PbxQueueSwitch.AutoOpenTimeKey)
            .ExecuteDeleteAsync());

    /// <summary>A clock set by the restaurant's local time on a fixed day, as the opening reads it.</summary>
    private sealed class LocalClock : TimeProvider
    {
        private static readonly DateTime Day = new(2030, 1, 15);
        private DateTimeOffset _now;

        public LocalClock(int hour, int minute) => Set(hour, minute);

        public void Set(int hour, int minute)
        {
            var local = DateTime.SpecifyKind(Day.AddHours(hour).AddMinutes(minute), DateTimeKind.Unspecified);
            _now = new DateTimeOffset(local, TimeZoneInfo.Local.GetUtcOffset(local)).ToUniversalTime();
        }

        public override DateTimeOffset GetUtcNow() => _now;
    }

    /// <summary>Answers, and the supervisor's request is cancelled while the PBX finishes.</summary>
    private sealed class DialerWhoseCallerLeaves(CancellationTokenSource caller) : IPbxFeatureDialer
    {
        public Task BlacklistAsync(PbxExtension from, BlacklistAction action, string number, CancellationToken ct) =>
            throw new InvalidOperationException("The queue switch never blacklists anything.");

        public Task ToggleAsync(PbxExtension from, string code, CancellationToken ct)
        {
            caller.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class NoteTakingDialer(string? failWith = null, bool answered = true) : IPbxFeatureDialer
    {
        public List<string> Codes { get; } = [];

        public Task BlacklistAsync(PbxExtension from, BlacklistAction action, string number, CancellationToken ct) =>
            throw new InvalidOperationException("The queue switch never blacklists anything.");

        public Task ToggleAsync(PbxExtension from, string code, CancellationToken ct)
        {
            Codes.Add(code);
            return failWith is null ? Task.CompletedTask : throw new PbxFeatureException(failWith, answered);
        }
    }
}
