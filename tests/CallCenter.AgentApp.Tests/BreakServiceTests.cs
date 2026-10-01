using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using CallCenter.AgentApp.Data;
using CallCenter.AgentApp.Services;
using CallCenter.AgentApp.Services.Calls;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Contracts.Breaks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.AgentApp.Tests;

/// <summary>
/// Break in and Break out (A-86): do not disturb goes with the break, the day's
/// total counts on, the limit warns without stopping anything, and a break the
/// server could not be told about waits in the buffer (A-04).
/// </summary>
public sealed class BreakServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"cc-breaks-{Guid.NewGuid():N}");
    private readonly AgentSession _session = new();
    private readonly List<(string Method, string Path, SaveBreakRequest? Body)> _sent = [];
    private readonly List<string> _notices = [];

    private DateTimeOffset _now = new(2026, 10, 1, 11, 0, 0, TimeZoneInfo.Local.GetUtcOffset(new DateTime(2026, 10, 1, 11, 0, 0)));

    /// <summary>What the stub server answers a break with: OK, down, or a 401.</summary>
    private Func<HttpResponseMessage> _breakAnswer = () => StubServer.Json(HttpStatusCode.OK, new { });

    private MyBreaksTodayDto? _today = new(10 * 60, 30);

    public BreakServiceTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_folder, recursive: true);
        }
        catch (IOException)
        {
            // Windows may still hold the file for a moment; it is in Temp.
        }
    }

    [Fact]
    public async Task Break_in_turns_do_not_disturb_on_and_break_out_turns_it_off()
    {
        var (breaks, preferences, settings, _) = await BuildAsync();

        await breaks.BreakInAsync();

        breaks.OnBreak.Should().BeTrue();
        preferences.DoNotDisturb.Should().BeTrue("no calls are offered during a break");
        settings.Current.BreakTurnedOnDoNotDisturb.Should().BeTrue();

        _now = _now.AddMinutes(4);
        await breaks.BreakOutAsync();

        breaks.OnBreak.Should().BeFalse();
        preferences.DoNotDisturb.Should().BeFalse();
        settings.Current.BreakTurnedOnDoNotDisturb.Should().BeFalse();

        _sent.Should().HaveCount(2);
        _sent[0].Body!.EndedAt.Should().BeNull();
        _sent[1].Body!.EndedAt.Should().Be(_now);
        _sent[1].Body!.EndedBy.Should().Be(BreakEndings.BreakOut);
        _sent[1].Path.Should().Be(_sent[0].Path, "the same break, sent whole twice");
    }

    [Fact]
    public async Task The_second_break_counts_on_from_where_the_first_stopped_and_from_what_the_server_had()
    {
        var (breaks, _, _, _) = await BuildAsync();

        breaks.TodayTotal().Should().Be(TimeSpan.FromMinutes(10), "the server's total for today, at sign-in");

        await breaks.BreakInAsync();
        _now = _now.AddMinutes(5);
        await breaks.BreakOutAsync();

        _now = _now.AddHours(1);
        breaks.TodayTotal().Should().Be(TimeSpan.FromMinutes(15), "time between breaks is not break");

        await breaks.BreakInAsync();
        _now = _now.AddMinutes(2);

        breaks.TodayTotal().Should().Be(TimeSpan.FromMinutes(17), "the break going counts as it goes");
    }

    [Fact]
    public async Task Past_the_limit_it_warns_and_break_in_still_works()
    {
        _today = new MyBreaksTodayDto(29 * 60, 30);
        var (breaks, preferences, _, _) = await BuildAsync();

        await breaks.BreakInAsync();
        breaks.OverLimit().Should().Be(TimeSpan.Zero);
        _notices.Should().BeEmpty();

        _now = _now.AddMinutes(3);
        breaks.OverLimit().Should().Be(TimeSpan.FromMinutes(2));
        await breaks.BreakOutAsync();

        await breaks.BreakInAsync();
        breaks.OnBreak.Should().BeTrue("the limit never stops a break");
        preferences.DoNotDisturb.Should().BeTrue();
        _notices.Should().Equal("breaks.overLimitNotice");
    }

    [Fact]
    public async Task With_the_server_down_the_break_waits_in_the_buffer_whole_and_goes_when_it_is_back()
    {
        _breakAnswer = () => throw new HttpRequestException("down");
        var (breaks, _, _, queue) = await BuildAsync();

        await breaks.BreakInAsync();
        _now = _now.AddMinutes(6);
        await breaks.BreakOutAsync();

        var waiting = await queue.PendingItemsAsync(_session.User!.Id);
        var item = waiting.Should().ContainSingle("the Break out replaced its Break in").Subject;
        item.Break!.Request.EndedAt.Should().Be(_now);
        item.Break.Request.SessionId.Should().Be(_session.SessionId);

        _breakAnswer = () => StubServer.Json(HttpStatusCode.OK, new { });
        _sent.Clear();
        var reporter = new CallLogReporter(Api(), _session, queue, new AgentNotices(), NullLogger<CallLogReporter>.Instance);
        await reporter.FlushAsync();

        _sent.Should().ContainSingle(s => s.Method == "PUT" && s.Body!.EndedBy == BreakEndings.BreakOut);
        (await queue.PendingItemsAsync(_session.User!.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_sign_in_that_has_ended_does_not_queue_the_break_the_server_already_ended()
    {
        _breakAnswer = () => StubServer.Status(HttpStatusCode.Unauthorized);
        var (breaks, preferences, _, queue) = await BuildAsync();

        await breaks.BreakInAsync();
        await breaks.EndForSignOutAsync();

        preferences.DoNotDisturb.Should().BeFalse("the break ended with the sign-in");
        (await queue.PendingItemsAsync(_session.User!.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task Signing_in_after_the_app_stopped_during_a_break_turns_do_not_disturb_off()
    {
        var (breaks, preferences, settings, _) = await BuildAsync();
        await breaks.BreakInAsync();

        // The app stops here, with no Break out. The next start reads the
        // same settings file.
        var restarted = new AgentSettingsStore(NullLogger<AgentSettingsStore>.Instance) { FilePath = settings.FilePath };
        var phone = new PhonePreferences(restarted, NullLogger<PhonePreferences>.Instance);
        phone.DoNotDisturb.Should().BeTrue();

        var again = new BreakService(Api(), _session, await QueueAsync(), phone, restarted, Notices(), NullLogger<BreakService>.Instance)
        {
            Now = () => _now,
        };
        await again.LoadAsync();

        again.OnBreak.Should().BeFalse("signing in ends the last sign-in's break");
        phone.DoNotDisturb.Should().BeFalse();
        restarted.Current.BreakTurnedOnDoNotDisturb.Should().BeFalse();
    }

    [Fact]
    public async Task Do_not_disturb_turned_on_by_hand_is_left_alone_at_sign_in()
    {
        var (_, preferences, settings, _) = await BuildAsync(signIn: false);
        preferences.DoNotDisturb = true;

        var breaks = new BreakService(Api(), _session, await QueueAsync(), preferences, settings, Notices(), NullLogger<BreakService>.Instance);
        SignIn();
        await breaks.LoadAsync();

        preferences.DoNotDisturb.Should().BeTrue("only a break's do not disturb is the break's to undo");
    }

    // ---- helpers ---------------------------------------------------------------

    private async Task<(BreakService Breaks, PhonePreferences Preferences, AgentSettingsStore Settings, CallLogQueue Queue)> BuildAsync(
        bool signIn = true)
    {
        var settings = new AgentSettingsStore(NullLogger<AgentSettingsStore>.Instance)
        {
            FilePath = Path.Combine(_folder, "settings.json"),
        };
        var preferences = new PhonePreferences(settings, NullLogger<PhonePreferences>.Instance);
        var queue = await QueueAsync();

        var breaks = new BreakService(Api(), _session, queue, preferences, settings, Notices(), NullLogger<BreakService>.Instance)
        {
            Now = () => _now,
        };

        if (signIn)
        {
            SignIn();
            await breaks.LoadAsync();
        }

        return (breaks, preferences, settings, queue);
    }

    private async Task<CallLogQueue> QueueAsync()
    {
        var queue = new CallLogQueue(new Buffer(Path.Combine(_folder, "agent-buffer.db")), _session,
            NullLogger<CallLogQueue>.Instance) { LegacyFile = null };
        await queue.InitialiseAsync();
        return queue;
    }

    private AgentNotices Notices()
    {
        var notices = new AgentNotices();
        notices.Posted += (_, key) => _notices.Add(key);
        return notices;
    }

    private void SignIn() =>
        _session.SignIn(new LoginResponse(
            "token", DateTimeOffset.Now.AddHours(12),
            new CurrentUserDto(Guid.NewGuid(), "agent-2001", "Agent", UserRoles.Agent),
            Guid.NewGuid(),
            new AgentExtensionsDto("pbx.test", "2001", "secret")));

    private ApiClient Api() => new StubServer(request =>
    {
        var path = request.RequestUri!.AbsolutePath;

        if (path.EndsWith("/api/breaks/mine/today", StringComparison.Ordinal))
        {
            return _today is null ? StubServer.Status(HttpStatusCode.InternalServerError) : StubServer.Json(HttpStatusCode.OK, _today);
        }

        if (path.StartsWith("/api/breaks/mine/", StringComparison.Ordinal))
        {
            var body = request.Content!.ReadFromJsonAsync<SaveBreakRequest>().GetAwaiter().GetResult();
            _sent.Add((request.Method.Method, path, body));
            return _breakAnswer();
        }

        return StubServer.Status(HttpStatusCode.NotFound);
    }).Client(_session);

    private sealed class Buffer(string path) : IDbContextFactory<AgentBufferDbContext>
    {
        public AgentBufferDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<AgentBufferDbContext>().UseSqlite($"Data Source={path};Pooling=False").Options);
    }
}
