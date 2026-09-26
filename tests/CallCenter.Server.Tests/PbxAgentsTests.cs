using System.Net;
using System.Net.Http.Json;
using System.Threading.Channels;
using CallCenter.Server.Features.Pbx;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Contracts.Pbx;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// What the PBX says the agents' phones are doing (S-61), and listening in on
/// a call (S-62). No PBX: the watch is written to directly, and the listener
/// plays packets it was handed.
/// </summary>
public class PbxNotifyTests
{
    // The bodies as Issabel (Asterisk 11) sent them on 26 Sep, from tools/presence-probe.

    private const string DialogFree = """
        <?xml version="1.0"?>
        <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="0" state="full" entity="sip:2001@192.168.0.27">
        <dialog id="2001">
        <state>terminated</state>
        </dialog>
        </dialog-info>
        """;

    private const string DialogInCall = """
        <?xml version="1.0"?>
        <dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" version="0" state="full" entity="sip:2001@192.168.0.27">
        <dialog id="2001">
        <state>confirmed</state>
        </dialog>
        </dialog-info>
        """;

    private const string PresenceOpen = """
        <?xml version="1.0" encoding="ISO-8859-1"?>
        <presence xmlns="urn:ietf:params:xml:ns:pidf"
        xmlns:pp="urn:ietf:params:xml:ns:pidf:person"
        xmlns:es="urn:ietf:params:xml:ns:pidf:rpid:status:rpid-status"
        xmlns:ep="urn:ietf:params:xml:ns:pidf:rpid:rpid-person"
        entity="sip:2011@192.168.0.27">
        <pp:person><status>
        </status></pp:person>
        <note>Ready</note>
        <tuple id="2001">
        <contact priority="1">sip:2001@192.168.0.27</contact>
        <status><basic>open</basic></status>
        </tuple>
        </presence>
        """;

    private const string PresenceClosed = """
        <?xml version="1.0" encoding="ISO-8859-1"?>
        <presence xmlns="urn:ietf:params:xml:ns:pidf"
        xmlns:pp="urn:ietf:params:xml:ns:pidf:person"
        xmlns:es="urn:ietf:params:xml:ns:pidf:rpid:status:rpid-status"
        xmlns:ep="urn:ietf:params:xml:ns:pidf:rpid:rpid-person"
        entity="sip:2011@192.168.0.27">
        <pp:person><status>
        <ep:activities><ep:away/></ep:activities>
        </status></pp:person>
        <note>Not online</note>
        <tuple id="2009">
        <contact priority="1">sip:2009@192.168.0.27</contact>
        <status><basic>closed</basic></status>
        </tuple>
        </presence>
        """;

    [Fact]
    public void The_dialog_report_says_free_ringing_or_in_a_call()
    {
        PbxNotify.ReadDialog(DialogFree).Should().Be(DialogState.Free);
        PbxNotify.ReadDialog(DialogInCall).Should().Be(DialogState.InCall);
        PbxNotify.ReadDialog(DialogInCall.Replace("confirmed", "early")).Should().Be(DialogState.Ringing);
        PbxNotify.ReadDialog("""<dialog-info xmlns="urn:ietf:params:xml:ns:dialog-info" state="full"/>""")
            .Should().Be(DialogState.Free, "no dialog at all is nothing going on");
    }

    [Fact]
    public void Talking_wins_over_a_second_call_ringing()
    {
        var two = DialogInCall.Replace("</dialog-info>", "<dialog id=\"x\"><state>early</state></dialog></dialog-info>");
        PbxNotify.ReadDialog(two).Should().Be(DialogState.InCall);
    }

    [Fact]
    public void The_presence_report_says_whether_a_phone_is_connected()
    {
        PbxNotify.ReadPresence(PresenceOpen).Should().BeTrue();
        PbxNotify.ReadPresence(PresenceClosed).Should().BeFalse();
    }

    [Fact]
    public void A_body_of_the_other_kind_or_none_or_not_xml_says_nothing()
    {
        PbxNotify.ReadDialog(PresenceOpen).Should().BeNull();
        PbxNotify.ReadPresence(DialogFree).Should().BeNull();
        PbxNotify.ReadDialog(null).Should().BeNull();
        PbxNotify.ReadPresence("<presence").Should().BeNull();
    }

    [Fact]
    public void Call_audio_is_turned_into_16_bit_samples()
    {
        // μ-law 0xFF and A-law 0xD5 are the silence the dialer itself sends.
        var mu = SipFeatureDialer.Pcm(0, [0xFF, 0x00])!;
        mu.Should().HaveCount(4);
        BitConverter.ToInt16(mu, 0).Should().Be(0);
        BitConverter.ToInt16(mu, 2).Should().BeLessThan(-30000);

        SipFeatureDialer.Pcm(8, [0xD5])!.Should().HaveCount(2);
        SipFeatureDialer.Pcm(101, [1, 2, 3]).Should().BeNull("a key event is not sound");
    }
}

public class ExtensionWatchTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2030, 1, 15, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void Presence_decides_offline_or_free_and_the_dialog_report_ringing_or_talking()
    {
        var clock = new Clock();
        var watch = new ExtensionWatch(clock);

        watch.Get("2001").State.Should().Be(PhoneStates.Unknown);

        // A switched-off phone: presence closed, dialog "terminated", which
        // would read as free on its own.
        watch.Presence("2001", connected: false);
        watch.Dialog("2001", DialogState.Free);
        watch.Get("2001").State.Should().Be(PhoneStates.Offline);

        watch.Presence("2001", connected: true);
        watch.Get("2001").State.Should().Be(PhoneStates.Free);

        watch.Dialog("2001", DialogState.Ringing);
        watch.Get("2001").State.Should().Be(PhoneStates.Ringing);

        clock.Now = clock.Now.AddSeconds(5);
        watch.Dialog("2001", DialogState.InCall);
        var (state, since) = watch.Get("2001");
        state.Should().Be(PhoneStates.InCall);
        since.Should().Be(clock.Now, "the call is timed from the answer");

        clock.Now = clock.Now.AddSeconds(30);
        watch.Presence("2001", connected: true);
        watch.Get("2001").Since.Should().Be(clock.Now.AddSeconds(-30), "a report that changes nothing does not restart the clock");
    }

    [Fact]
    public void A_report_nobody_renewed_turns_unknown_rather_than_showing_an_old_call()
    {
        var clock = new Clock();
        var watch = new ExtensionWatch(clock);

        watch.Heard();
        watch.Presence("2001", connected: true);
        watch.Dialog("2001", DialogState.InCall);
        watch.IsLive.Should().BeTrue();

        clock.Now = clock.Now.Add(ExtensionWatch.StaleAfter).AddSeconds(1);
        watch.Get("2001").State.Should().Be(PhoneStates.Unknown);
        watch.IsLive.Should().BeFalse();
        watch.Problem.Should().Be(ExtensionWatch.Problems.NoAnswer);

        // A renewal the PBX accepted keeps the last word fresh.
        watch.Heard();
        watch.Presence("2001", connected: true);
        watch.Dialog("2001", DialogState.Free);
        clock.Now = clock.Now.AddMinutes(2);
        watch.Confirmed("2001", presence: true);
        watch.Confirmed("2001", presence: false);
        watch.Get("2001").State.Should().Be(PhoneStates.Free);
    }

    [Fact]
    public void Without_the_servers_extension_nothing_is_known()
    {
        var watch = new ExtensionWatch(new Clock());
        watch.Heard();
        watch.Presence("2001", connected: true);

        watch.Failing(ExtensionWatch.Problems.NotConfigured);

        watch.IsLive.Should().BeFalse();
        watch.Problem.Should().Be(ExtensionWatch.Problems.NotConfigured);
        watch.Get("2001").State.Should().Be(PhoneStates.Unknown);
    }
}

[Collection(ApiCollection.Name)]
public class PbxAgentsTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task The_supervisor_sees_each_agents_phone_as_the_PBX_reports_it_and_the_dashboard_counts_the_calls()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var talking = await data.CreateUserAsync();
        var off = await data.CreateUserAsync();
        var watch = factory.RealAccounts.Services.GetRequiredService<ExtensionWatch>();

        try
        {
            watch.Heard();
            watch.Presence(talking.Extension!, connected: true);
            watch.Dialog(talking.Extension!, DialogState.InCall);
            watch.Presence(off.Extension!, connected: false);

            var phones = (await supervisor.GetFromJsonAsync<AgentPhonesDto>("/api/pbx/agents"))!;
            phones.Live.Should().BeTrue();
            phones.Problem.Should().BeNull();

            var t = phones.Agents.Single(a => a.UserId == talking.Id);
            (t.Extension, t.State, t.DisplayName).Should().Be((talking.Extension, PhoneStates.InCall, talking.DisplayName));
            t.Since.Should().NotBeNull();
            phones.Agents.Single(a => a.UserId == off.Id).State.Should().Be(PhoneStates.Offline);

            var today = (await supervisor.GetFromJsonAsync<DashboardTodayDto>("/api/reports/dashboard/today"))!;
            today.FromPbx.Should().BeTrue();
            today.AgentsInCall.Should().BeGreaterThanOrEqualTo(1);

            // Agents do not see each other's phones.
            var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
            (await agent.GetAsync("/api/pbx/agents")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }
        finally
        {
            // Other tests count agents online from sign-ins, as a host with no
            // PBX watch does.
            watch.Failing(ExtensionWatch.Problems.NotStarted);
        }
    }

    [DatabaseFact]
    public async Task Without_the_servers_extension_listening_says_so_and_dials_nothing()
    {
        await CleanUpAsync();
        var listener = new PlayingListener();
        await using var host = HostWith(listener);
        var (supervisor, _) = await SignInAsync(host, await data.CreateUserAsync(UserRoles.Supervisor));
        var agent = await data.CreateUserAsync();

        var response = await supervisor.GetAsync($"/api/pbx/agents/{agent.Id}/listen");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("not_configured");
        listener.Codes.Should().BeEmpty();
    }

    [DatabaseFact]
    public async Task Listening_dials_222_and_the_extension_passes_the_sound_on_and_is_audited()
    {
        var listener = new PlayingListener(packets: 12, endAfter: true);
        await using var host = HostWith(listener);
        var (supervisor, login) = await SignInAsync(host, await data.CreateUserAsync(UserRoles.Supervisor));
        var agent = await data.CreateUserAsync();

        try
        {
            await SetLineAsync(supervisor);

            using var response = await supervisor.GetAsync(
                $"/api/pbx/agents/{agent.Id}/listen", HttpCompletionOption.ResponseHeadersRead);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.Contains(PbxAgentsController.ListenIdHeader).Should().BeTrue();
            var sound = await response.Content.ReadAsByteArrayAsync();

            listener.Codes.Should().Equal($"*222{agent.Extension}");
            sound.Should().HaveCount(12 * PlayingListener.PacketBytes, "every packet, and nothing else");
            sound.Take(2).Should().Equal(1, 0);

            var audit = await AuditAsync(login.User.Id, agent.Id);
            audit.Should().Equal("start", "stop");
            listener.HungUp.Should().BeTrue();
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task Stop_ends_the_listen_in_and_hangs_up()
    {
        var listener = new PlayingListener(packets: 5, endAfter: false);
        await using var host = HostWith(listener);
        var (supervisor, login) = await SignInAsync(host, await data.CreateUserAsync(UserRoles.Supervisor));
        var agent = await data.CreateUserAsync();

        try
        {
            await SetLineAsync(supervisor);

            using var response = await supervisor.GetAsync(
                $"/api/pbx/agents/{agent.Id}/listen", HttpCompletionOption.ResponseHeadersRead);
            var id = response.Headers.GetValues(PbxAgentsController.ListenIdHeader).Single();

            // Another supervisor cannot stop it.
            var (other, _) = await SignInAsync(host, await data.CreateUserAsync(UserRoles.Supervisor));
            (await other.DeleteAsync($"/api/pbx/agents/listen/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

            (await supervisor.DeleteAsync($"/api/pbx/agents/listen/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            // The stream ends rather than waiting for a call that never hangs up.
            var read = response.Content.ReadAsByteArrayAsync();
            (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(read);

            await WaitForAsync(() => listener.HungUp);
            (await AuditAsync(login.User.Id, agent.Id)).Should().Equal("start", "stop");
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>A host like the real-accounts one, listening through <paramref name="listener"/>.</summary>
    private Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> HostWith(IPbxCallListener listener) =>
        factory.RealAccounts.WithWebHostBuilder(b => b.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPbxCallListener>();
            services.AddSingleton(listener);
        }));

    private static async Task<(HttpClient Client, Shared.Contracts.Auth.LoginResponse Login)> SignInAsync(
        Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> host, Data.Entities.User user)
    {
        var client = host.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new Shared.Contracts.Auth.LoginRequest(user.Login, TestData.Password));
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<Shared.Contracts.Auth.LoginResponse>())!;
        client.DefaultRequestHeaders.Authorization = new("Bearer", login.AccessToken);
        return (client, login);
    }

    private static async Task SetLineAsync(HttpClient supervisor) =>
        (await supervisor.PutAsJsonAsync("/api/pbx/blacklist", new UpdatePbxBlacklistRequest("7999", "secret-for-tests")))
            .EnsureSuccessStatusCode();

    private Task<List<string>> AuditAsync(Guid supervisorId, Guid agentId) =>
        data.QueryAsync(db => db.AuditLog
            .Where(a => a.UserId == supervisorId && a.Entity == "listen" && a.EntityId == agentId.ToString())
            .OrderBy(a => a.Id)
            .Select(a => a.Action)
            .ToListAsync());

    private Task CleanUpAsync() =>
        data.QueryAsync(db => db.Settings.Where(s => s.Key.StartsWith(PbxFeatureLine.Keys.Prefix)).ExecuteDeleteAsync());

    private static async Task WaitForAsync(Func<bool> done)
    {
        for (var i = 0; i < 100 && !done(); i++)
        {
            await Task.Delay(50);
        }

        done().Should().BeTrue();
    }

    /// <summary>Answers at once and plays numbered packets; ends the call after them, or never.</summary>
    private sealed class PlayingListener(int packets = 0, bool endAfter = true) : IPbxCallListener
    {
        public const int PacketBytes = 320;

        public List<string> Codes { get; } = [];
        public bool HungUp { get; private set; }

        public Task<IPbxListenCall> ListenAsync(PbxExtension from, string code, CancellationToken ct)
        {
            Codes.Add(code);

            var audio = Channel.CreateUnbounded<byte[]>();
            for (var i = 1; i <= packets; i++)
            {
                var packet = new byte[PacketBytes];
                packet[0] = (byte)i;
                audio.Writer.TryWrite(packet);
            }

            var ended = new TaskCompletionSource();
            if (endAfter)
            {
                audio.Writer.Complete();
                ended.SetResult();
            }

            return Task.FromResult<IPbxListenCall>(new Call(audio, ended, () => HungUp = true));
        }

        private sealed class Call(Channel<byte[]> audio, TaskCompletionSource ended, Action hangUp) : IPbxListenCall
        {
            public ChannelReader<byte[]> Audio => audio.Reader;
            public Task Ended => ended.Task;

            public ValueTask DisposeAsync()
            {
                audio.Writer.TryComplete();
                ended.TrySetResult();
                hangUp();
                return ValueTask.CompletedTask;
            }
        }
    }
}
