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

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 8)]
    public void The_supervisors_voice_comes_back_out_of_G711_as_it_went_in(bool alaw, int payloadType)
    {
        short[] samples = [0, 1000, -1000, 12000, -12000, 32000, -32000];
        var payload = new byte[samples.Length];

        SipFeatureDialer.G711(samples, payload, alaw);
        var back = SipFeatureDialer.Pcm(payloadType, payload)!;

        for (var i = 0; i < samples.Length; i++)
        {
            // G.711 keeps about 3% of a loud sample and a few steps of a quiet one.
            int heard = BitConverter.ToInt16(back, i * 2);
            heard.Should().BeCloseTo(samples[i], (uint)Math.Max(16, Math.Abs(samples[i] / 16)));
        }

        SipFeatureDialer.G711([0], payload.AsSpan(0, 1), alaw);
        payload[0].Should().Be(alaw ? (byte)0xD5 : (byte)0xFF, "silence encodes as the silence the dialer sends");
    }
}

public class VoiceBufferTests
{
    private static byte[] Samples(int count, short value)
    {
        var bytes = new byte[count * 2];
        for (var i = 0; i < count; i++)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(i * 2), value);
        }

        return bytes;
    }

    [Fact]
    public void Nothing_is_said_until_the_lead_has_arrived_then_it_comes_out_in_order()
    {
        var voice = new VoiceBuffer();
        var packet = new short[160];

        voice.Write(Samples(VoiceBuffer.Lead - 1, 7));
        voice.Read(packet).Should().BeFalse("a burst is held back until the lead is there");

        voice.Write(Samples(1, 9));
        voice.Read(packet).Should().BeTrue();
        packet.Should().AllSatisfy(s => s.Should().Be(7));

        // Once playing, a packet's worth is enough.
        while (voice.Count >= packet.Length)
        {
            voice.Read(packet).Should().BeTrue();
        }

        packet[^1].Should().Be(9);
    }

    [Fact]
    public void Running_dry_waits_for_the_lead_again()
    {
        var voice = new VoiceBuffer();
        var packet = new short[160];

        voice.Write(Samples(VoiceBuffer.Lead, 1));
        for (var i = 0; i < VoiceBuffer.Lead / packet.Length; i++)
        {
            voice.Read(packet).Should().BeTrue();
        }

        voice.Read(packet).Should().BeFalse("ran dry");

        voice.Write(Samples(packet.Length, 2));
        voice.Read(packet).Should().BeFalse("one packet after a gap is not yet the lead");
    }

    [Fact]
    public void A_backlog_past_a_second_loses_its_oldest_samples()
    {
        var voice = new VoiceBuffer();
        voice.Write(Samples(VoiceBuffer.MaxSamples, 1));
        voice.Write(Samples(160, 2));

        voice.Count.Should().Be(VoiceBuffer.MaxSamples);

        var packet = new short[160];
        while (voice.Count > packet.Length)
        {
            voice.Read(packet);
        }

        voice.Read(packet).Should().BeTrue();
        packet.Should().AllSatisfy(s => s.Should().Be(2), "the newest is kept");
    }

    [Fact]
    public void An_odd_last_byte_is_left_out()
    {
        var voice = new VoiceBuffer();
        voice.Write([1, 0, 2]);
        voice.Count.Should().Be(1);
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

    [DatabaseFact]
    public async Task The_listen_in_ends_when_the_call_does_although_222_stays_on_the_line()
    {
        // As on the server, 26 Sep: the PBX's spy never hangs up by itself.
        var listener = new PlayingListener(packets: 5, endAfter: false);
        await using var host = HostWith(listener);
        var (supervisor, login) = await SignInAsync(host, await data.CreateUserAsync(UserRoles.Supervisor));
        var agent = await data.CreateUserAsync();
        var watch = host.Services.GetRequiredService<ExtensionWatch>();

        try
        {
            await SetLineAsync(supervisor);
            watch.Heard();
            watch.Presence(agent.Extension!, connected: true);
            watch.Dialog(agent.Extension!, DialogState.InCall);

            using var response = await supervisor.GetAsync(
                $"/api/pbx/agents/{agent.Id}/listen", HttpCompletionOption.ResponseHeadersRead);
            response.StatusCode.Should().Be(HttpStatusCode.OK);

            // The agent hangs up.
            watch.Dialog(agent.Extension!, DialogState.Free);

            var read = response.Content.ReadAsByteArrayAsync();
            (await Task.WhenAny(read, Task.Delay(TimeSpan.FromSeconds(10)))).Should().BeSameAs(read,
                "the server ends the listen-in within a few seconds of the call ending");

            await WaitForAsync(() => listener.HungUp);
            var why = await data.QueryAsync(db => db.AuditLog
                .Where(a => a.UserId == login.User.Id && a.Entity == "listen" && a.Action == "stop")
                .Select(a => a.After!.RootElement.GetProperty("why").GetString())
                .SingleAsync());
            why.Should().Be("call_ended");
        }
        finally
        {
            watch.Failing(ExtensionWatch.Problems.NotStarted);
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task Speaking_dials_223_sends_the_supervisors_voice_into_the_call_and_is_audited_as_speaking()
    {
        var listener = new PlayingListener(packets: 5, endAfter: false);
        await using var host = HostWith(listener);
        var (supervisor, login) = await SignInAsync(host, await data.CreateUserAsync(UserRoles.Supervisor));
        var agent = await data.CreateUserAsync();

        try
        {
            await SetLineAsync(supervisor);

            using var response = await supervisor.GetAsync(
                $"/api/pbx/agents/{agent.Id}/speak", HttpCompletionOption.ResponseHeadersRead);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var id = response.Headers.GetValues(PbxAgentsController.ListenIdHeader).Single();
            listener.Codes.Should().Equal($"*223{agent.Extension}");

            byte[] first = [1, 0, 2, 0], second = [3, 0];
            (await supervisor.PostAsync($"/api/pbx/agents/listen/{id}/voice", new ByteArrayContent(first)))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await supervisor.PostAsync($"/api/pbx/agents/listen/{id}/voice", new ByteArrayContent(second)))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            listener.Spoken.Should().Equal(1, 0, 2, 0, 3, 0);

            // Nobody else's voice goes into it.
            var (other, _) = await SignInAsync(host, await data.CreateUserAsync(UserRoles.Supervisor));
            (await other.PostAsync($"/api/pbx/agents/listen/{id}/voice", new ByteArrayContent(first)))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);

            // More than a second at once is refused.
            (await supervisor.PostAsync($"/api/pbx/agents/listen/{id}/voice",
                    new ByteArrayContent(new byte[PbxAgentsController.MaxVoiceBytes + 2])))
                .StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
            listener.Spoken.Should().HaveCount(6);

            (await supervisor.DeleteAsync($"/api/pbx/agents/listen/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

            // The stream ends once the server has hung up and written the stop down.
            await response.Content.ReadAsByteArrayAsync();
            listener.HungUp.Should().BeTrue();

            var speak = await data.QueryAsync(db => db.AuditLog
                .Where(a => a.UserId == login.User.Id && a.Entity == "listen" && a.EntityId == agent.Id.ToString())
                .OrderBy(a => a.Id)
                .Select(a => a.After!.RootElement.GetProperty("speak").GetBoolean())
                .ToListAsync());
            speak.Should().Equal(true, true);

            // Once it has ended, the voice has nowhere to go.
            (await supervisor.PostAsync($"/api/pbx/agents/listen/{id}/voice", new ByteArrayContent(first)))
                .StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task A_listen_only_222_call_takes_no_voice()
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

            (await supervisor.PostAsync($"/api/pbx/agents/listen/{id}/voice", new ByteArrayContent([1, 0])))
                .StatusCode.Should().Be(HttpStatusCode.Conflict);
            listener.Spoken.Should().BeEmpty();

            (await supervisor.DeleteAsync($"/api/pbx/agents/listen/{id}")).EnsureSuccessStatusCode();
            await response.Content.ReadAsByteArrayAsync();
            listener.HungUp.Should().BeTrue();
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

            return Task.FromResult<IPbxListenCall>(new Call(audio, ended, () => HungUp = true, Spoken));
        }

        /// <summary>Everything the supervisor said, in order.</summary>
        public List<byte> Spoken { get; } = [];

        private sealed class Call(Channel<byte[]> audio, TaskCompletionSource ended, Action hangUp, List<byte> spoken) : IPbxListenCall
        {
            public ChannelReader<byte[]> Audio => audio.Reader;
            public Task Ended => ended.Task;

            public void Speak(ReadOnlySpan<byte> pcm)
            {
                lock (spoken)
                {
                    spoken.AddRange(pcm.ToArray());
                }
            }

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
