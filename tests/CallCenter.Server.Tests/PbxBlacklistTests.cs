using System.Net;
using System.Net.Http.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Pbx;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Contacts;
using CallCenter.Shared.Contracts.Pbx;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The PBX blacklist (S-46): the prompts heard, the keys pressed, and the
/// PBX following the Blocked flag — added once, removed once, a failure tried
/// again later.
/// </summary>
/// <remarks>
/// No PBX here. The dialer is replaced by one that only takes notes, and the
/// test host has no blacklist job, so nothing dials by itself. The sync runs
/// over the whole shared database and so also "dials" other tests' blocked
/// numbers; each test asserts on its own numbers only.
/// </remarks>
[Collection(ApiCollection.Name)]
public class PbxBlacklistTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    // ---- hearing the prompts ----------------------------------------------------

    [Fact]
    public void A_prompt_has_ended_once_it_has_been_quiet_for_two_seconds()
    {
        var clock = new ManualClock();
        var listener = new PromptListener(clock);

        listener.Hear(3000);
        clock.Advance(TimeSpan.FromSeconds(1));
        listener.Hear(2500);

        clock.Advance(TimeSpan.FromSeconds(1.5));
        listener.State().Should().Be(PromptListener.Heard.Waiting, "a pause between words is not the end");

        clock.Advance(TimeSpan.FromSeconds(0.5));
        listener.State().Should().Be(PromptListener.Heard.Ended);
    }

    [Fact]
    public void Silence_before_the_prompt_starts_is_not_its_end()
    {
        var clock = new ManualClock();
        var listener = new PromptListener(clock);

        listener.Hear(0);
        clock.Advance(TimeSpan.FromSeconds(5));

        listener.State().Should().Be(PromptListener.Heard.Waiting, "the PBX may take a moment to start talking");

        clock.Advance(PromptListener.NoSoundLimit);
        listener.State().Should().Be(PromptListener.Heard.NoSound);
    }

    [Fact]
    public void Listening_again_forgets_the_previous_prompt()
    {
        var clock = new ManualClock();
        var listener = new PromptListener(clock);

        listener.Hear(3000);
        clock.Advance(TimeSpan.FromSeconds(3));
        listener.State().Should().Be(PromptListener.Heard.Ended);

        listener.Listen();
        listener.State().Should().Be(PromptListener.Heard.Waiting,
            "the quiet after the first prompt must not count as the end of the read-back");
    }

    [Fact]
    public void A_prompt_that_never_stops_is_given_up_on()
    {
        var clock = new ManualClock();
        var listener = new PromptListener(clock);

        for (var i = 0; i < 50; i++)
        {
            listener.Hear(3000);
            clock.Advance(TimeSpan.FromSeconds(1));
        }

        listener.State().Should().Be(PromptListener.Heard.TooLong);
    }

    [Fact]
    public void Digital_silence_is_quiet_and_speech_is_loud_in_both_codecs()
    {
        // μ-law 0xFF and A-law 0xD5 are zero; 0x80 and 0xAA are near the top.
        PromptListener.Level(0, Enumerable.Repeat((byte)0xFF, 160).ToArray()).Should().Be(0);
        PromptListener.Level(8, Enumerable.Repeat((byte)0xD5, 160).ToArray()).Should().BeLessThan(PromptListener.LoudLevel);

        PromptListener.Level(0, Enumerable.Repeat((byte)0x80, 160).ToArray()).Should().BeGreaterThan(PromptListener.LoudLevel);
        PromptListener.Level(8, Enumerable.Repeat((byte)0xAA, 160).ToArray()).Should().BeGreaterThan(PromptListener.LoudLevel);
    }

    [Fact]
    public void A_key_press_or_comfort_noise_says_nothing_about_the_prompt()
    {
        PromptListener.Level(101, [0x01, 0x0A, 0x00, 0xA0]).Should().BeNull();
        PromptListener.Level(13, [0x40]).Should().BeNull();
    }

    // ---- the keys and the call --------------------------------------------------

    [Fact]
    public void Keys_are_sent_as_their_RFC_4733_events()
    {
        "0123456789*#".Select(SipFeatureDialer.Tone).Should().Equal(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11);
    }

    [Fact]
    public void The_call_offers_G711_and_key_events()
    {
        var rtp = SipFeatureDialer.CreateMedia();
        try
        {
            var sdp = rtp.CreateOffer(IPAddress.Loopback).ToString();

            sdp.Should().Contain("PCMU/8000").And.Contain("PCMA/8000");
            sdp.Should().Contain("telephone-event/8000", "the keys go as RFC 2833 events, which have to be offered");
        }
        finally
        {
            rtp.Close("test");
        }
    }

    [Fact]
    public async Task Only_digits_are_keyed_in()
    {
        var dialer = new SipFeatureDialer(TimeProvider.System, NullLogger<SipFeatureDialer>.Instance);

        var act = () => dialer.BlacklistAsync(new PbxExtension("192.0.2.10", "7999", "x"), BlacklistAction.Add, "+970599", default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    // ---- what to dial -----------------------------------------------------------

    [Fact]
    public void The_number_keyed_in_is_the_local_form()
    {
        PbxBlacklistSync.PbxNumber("970599123456").Should().Be("0599123456");
        PbxBlacklistSync.PbxNumber("97022345678").Should().Be("022345678");
        PbxBlacklistSync.PbxNumber("2001").Should().BeNull("an extension is not a caller the PBX can blacklist");
        PbxBlacklistSync.PbxNumber("962791234567").Should().BeNull("a foreign number has no local form");
    }

    [Fact]
    public void Blocked_numbers_not_on_the_PBX_are_added_and_unblocked_ones_it_has_are_removed()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new List<PbxBlacklistEntry>
        {
            new() { Number = "0599000001", OnPbx = true },
            new() { Number = "0599000002", OnPbx = true },
        };

        var plan = PbxBlacklistSync.Plan(new HashSet<string> { "0599000001", "0599000003" }, rows, now);

        plan.Should().Equal(("0599000003", BlacklistAction.Add), ("0599000002", BlacklistAction.Remove));
    }

    [Fact]
    public void A_number_that_failed_is_tried_again_after_the_retry_delay_and_not_before()
    {
        var now = DateTimeOffset.UtcNow;
        var wanted = new HashSet<string> { "0599000001" };
        List<PbxBlacklistEntry> Failed(TimeSpan ago) =>
            [new() { Number = "0599000001", LastError = "busy", LastAttemptAt = now - ago, FailedAttempts = 1 }];

        PbxBlacklistSync.Plan(wanted, Failed(TimeSpan.FromMinutes(1)), now).Should().BeEmpty();
        PbxBlacklistSync.Plan(wanted, Failed(PbxBlacklistSync.RetryAfter), now)
            .Should().Equal(("0599000001", BlacklistAction.Add));
    }

    // ---- the sync, against the database -----------------------------------------

    [DatabaseFact]
    public async Task Blocking_puts_a_number_on_the_PBX_once_and_unblocking_takes_it_off()
    {
        var mobile = TestData.NewMobile();
        var contact = await data.CreateContactAsync(null, mobile);
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var dialer = new NoteTakingDialer();

        await SetUpExtensionAsync(client);
        try
        {
            await FlagAsync(client, contact.Id, blocked: true);

            await SyncAsync(dialer);
            await SyncAsync(dialer);

            dialer.Calls.Where(c => c.Number == mobile).Should().Equal(
                [(BlacklistAction.Add, mobile)], "a number already on the PBX is not added again");
            (await RowAsync(mobile))!.OnPbx.Should().BeTrue();

            await FlagAsync(client, contact.Id, blocked: false);
            await SyncAsync(dialer);

            dialer.Calls.Where(c => c.Number == mobile).Should().Equal(
                (BlacklistAction.Add, mobile), (BlacklistAction.Remove, mobile));
            (await RowAsync(mobile)).Should().BeNull();
        }
        finally
        {
            await CleanUpAsync(mobile);
        }
    }

    [DatabaseFact]
    public async Task A_failed_call_is_shown_and_tried_again_later()
    {
        var mobile = TestData.NewMobile();
        var contact = await data.CreateContactAsync(null, mobile);
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var clock = new ManualClock();

        await SetUpExtensionAsync(client);
        try
        {
            await FlagAsync(client, contact.Id, blocked: true);

            await SyncAsync(new NoteTakingDialer(failWith: "The PBX did not answer *30."), clock);

            var status = await client.GetFromJsonAsync<PbxBlacklistDto>("/api/pbx/blacklist");
            status!.Failures.Should().ContainSingle(f => f.Number == mobile)
                .Which.Should().Match<PbxBlacklistFailureDto>(f => f.Adding && f.Attempts == 1 && f.Error.Contains("did not answer"));

            var again = new NoteTakingDialer();
            await SyncAsync(again, clock);
            again.Calls.Should().NotContain(c => c.Number == mobile, "the retry delay has not passed");

            clock.Advance(PbxBlacklistSync.RetryAfter);
            await SyncAsync(again, clock);
            again.Calls.Should().Contain((BlacklistAction.Add, mobile));

            var row = await RowAsync(mobile);
            row!.OnPbx.Should().BeTrue();
            row.LastError.Should().BeNull();
            row.FailedAttempts.Should().Be(0);
        }
        finally
        {
            await CleanUpAsync(mobile);
        }
    }

    [DatabaseFact]
    public async Task The_password_never_comes_back_and_an_extension_must_be_digits()
    {
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var bad = await client.PutAsJsonAsync("/api/pbx/blacklist", new UpdatePbxBlacklistRequest("ext7999", "x"));
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        await SetUpExtensionAsync(client);
        try
        {
            var body = await client.GetStringAsync("/api/pbx/blacklist");
            body.Should().NotContain("secret-for-tests");

            var status = await client.GetFromJsonAsync<PbxBlacklistDto>("/api/pbx/blacklist");
            status!.SecretSet.Should().BeTrue();
            status.Extension.Should().Be("7999");
        }
        finally
        {
            await CleanUpAsync();
        }
    }

    [DatabaseFact]
    public async Task Agents_cannot_see_or_change_the_blacklist_settings()
    {
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());

        (await agent.GetAsync("/api/pbx/blacklist")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await agent.PutAsJsonAsync("/api/pbx/blacklist", new UpdatePbxBlacklistRequest("7999", "x")))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- helpers ----------------------------------------------------------------

    private static async Task SetUpExtensionAsync(HttpClient client) =>
        (await client.PutAsJsonAsync("/api/pbx/blacklist", new UpdatePbxBlacklistRequest("7999", "secret-for-tests")))
            .EnsureSuccessStatusCode();

    private static async Task FlagAsync(HttpClient client, Guid contactId, bool blocked) =>
        (await client.PutAsJsonAsync($"/api/contacts/{contactId}/flags",
            new SetContactFlagsRequest(false, blocked, blocked ? "Nuisance calls" : null))).EnsureSuccessStatusCode();

    /// <summary>One sync pass with <paramref name="dialer"/>, on the host with a PBX address.</summary>
    private async Task SyncAsync(IPbxFeatureDialer dialer, TimeProvider? clock = null)
    {
        await using var scope = factory.RealAccounts.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;

        var sync = new PbxBlacklistSync(
            services.GetRequiredService<CallCenterDbContext>(),
            dialer,
            services.GetRequiredService<PbxFeatureLine>(),
            services.GetRequiredService<ISipSecretProtector>(),
            new PbxBlacklistGate(),
            clock ?? TimeProvider.System,
            NullLogger<PbxBlacklistSync>.Instance);

        await sync.RunAsync();
    }

    private Task<PbxBlacklistEntry?> RowAsync(string number) =>
        data.QueryAsync(db => db.PbxBlacklist.AsNoTracking().SingleOrDefaultAsync(r => r.Number == number));

    /// <summary>The extension off again, and this test's numbers out of the table.</summary>
    private Task CleanUpAsync(params string[] numbers) =>
        data.QueryAsync(async db =>
        {
            await db.Settings.Where(s => s.Key.StartsWith(PbxBlacklistSync.Keys.Prefix)
                || s.Key.StartsWith(PbxFeatureLine.Keys.Prefix)).ExecuteDeleteAsync();
            return await db.PbxBlacklist.Where(r => numbers.Contains(r.Number)).ExecuteDeleteAsync();
        });

    private sealed class NoteTakingDialer(string? failWith = null) : IPbxFeatureDialer
    {
        public List<(BlacklistAction Action, string Number)> Calls { get; } = [];

        public Task BlacklistAsync(PbxExtension from, BlacklistAction action, string number, CancellationToken ct)
        {
            Calls.Add((action, number));
            return failWith is null ? Task.CompletedTask : throw new PbxFeatureException(failWith);
        }

        public Task ToggleAsync(PbxExtension from, string code, CancellationToken ct) =>
            throw new InvalidOperationException("The blacklist never toggles anything.");
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
