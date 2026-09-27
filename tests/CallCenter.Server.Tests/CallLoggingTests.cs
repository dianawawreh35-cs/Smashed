using System.Net;
using System.Net.Http.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// What <c>POST /api/communications/calls</c> does with a call once it is past
/// the door, against a real database (A-13, A-14, A-17).
/// </summary>
/// <remarks>
/// <see cref="CommunicationsEndpointTests"/> checks who may file a call. These
/// check what is stored: which customer the number lands on, that a resend is
/// an update rather than a second call, and that every outcome survives the trip.
/// </remarks>
[Collection(ApiCollection.Name)]
public class CallLoggingTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    /// <summary>
    /// The forms a Palestinian mobile arrives in. The contact below is saved as
    /// a customer would dictate it, <c>059 xxx xxxx</c> with spaces; the PBX
    /// announces <c>+970…</c>; the old system stored the bare nine digits.
    /// </summary>
    public static TheoryData<string> Forms => new()
    {
        "{0}",                          // 0599123456, as saved
        "+970{1}",                      // what the PBX announces
        "00970{1}",                     // dialled internationally
        "{1}",                          // 599123456, the old system's export
        "+972{1}",                      // the Israeli plan: matched on the last nine digits
        "٠{2}",                         // Arabic-Indic digits, as typed on an Arabic keyboard
    };

    [DatabaseTheory]
    [MemberData(nameof(Forms))]
    public async Task A_call_is_matched_to_its_contact_whatever_form_the_number_arrives_in(string form)
    {
        await data.EnsurePhoneChannelAsync();
        var mobile = TestData.NewMobile();
        var contact = await data.CreateContactAsync("Test customer", $"{mobile[..3]} {mobile[3..6]} {mobile[6..]}");
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync());

        var number = string.Format(form, mobile, mobile[1..], ToArabicIndic(mobile[1..]));
        var call = await LogAsync(client, Call(number));

        call.ContactId.Should().Be(contact.Id, $"{number} is the same phone as {mobile}");
        call.ContactName.Should().Be("Test customer");
    }

    [DatabaseFact]
    public async Task A_call_from_a_number_nobody_has_is_stored_with_no_contact()
    {
        await data.EnsurePhoneChannelAsync();
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync());

        var call = await LogAsync(client, Call(TestData.NewMobile()));

        call.ContactId.Should().BeNull();

        var stored = await StoredAsync(call.Id);
        stored.ContactId.Should().BeNull();
        stored.RemoteNormalised.Should().StartWith("9705", "the number is still kept, normalised, for later");
    }

    [DatabaseFact]
    public async Task A_call_from_a_deleted_contact_is_not_matched_to_it()
    {
        await data.EnsurePhoneChannelAsync();
        var mobile = TestData.NewMobile();
        var contact = await data.CreateContactAsync("Gone", mobile);
        await data.QueryAsync(async db =>
        {
            await db.Contacts.Where(c => c.Id == contact.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, DateTimeOffset.UtcNow));
            return 0;
        });
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync());

        var call = await LogAsync(client, Call(mobile));

        call.ContactId.Should().BeNull("a deleted contact appears nowhere, so a call must not land on it");
    }

    [DatabaseFact]
    public async Task Reporting_the_same_call_twice_updates_it_rather_than_adding_a_second()
    {
        // The offline queue resends without checking what already arrived
        // (A-04). That is only safe because the pair (Call-ID, extension) is the
        // key, so the resend overwrites the first report.
        await data.EnsurePhoneChannelAsync();
        var agent = await data.CreateUserAsync();
        var (client, _) = await data.SignInAsync(agent);
        var sipCallId = TestData.NewSipCallId();
        var started = DateTimeOffset.UtcNow.AddMinutes(-3);

        var first = await LogAsync(client, Call(TestData.NewMobile(), sipCallId: sipCallId,
            extension: agent.Extension!, status: CommunicationStatuses.Missed, started: started));

        var second = await LogAsync(client, Call(TestData.NewMobile(), sipCallId: sipCallId,
            extension: agent.Extension!, status: CommunicationStatuses.Answered, started: started,
            answered: started.AddSeconds(10), ended: started.AddSeconds(100)));

        second.Id.Should().Be(first.Id);
        second.Status.Should().Be(CommunicationStatuses.Answered, "the later report is the truth");
        second.DurationSec.Should().Be(90, "talk time, from answer to end");

        var rows = await data.QueryAsync(db =>
            db.Communications.CountAsync(c => c.SipCallId == sipCallId));
        rows.Should().Be(1);
    }

    [DatabaseFact]
    public async Task The_same_Call_ID_on_another_extension_is_another_call()
    {
        // A call offered to two agents in turn reaches both laptops with one
        // Call-ID. Each laptop's report is its own row, or the second agent's
        // would overwrite the first's.
        await data.EnsurePhoneChannelAsync();
        var (one, _) = await data.SignInAsync(await data.CreateUserAsync());
        var (two, _) = await data.SignInAsync(await data.CreateUserAsync());
        var sipCallId = TestData.NewSipCallId();

        var first = await LogAsync(one, Call(TestData.NewMobile(), sipCallId: sipCallId));
        var second = await LogAsync(two, Call(TestData.NewMobile(), sipCallId: sipCallId));

        second.Id.Should().NotBe(first.Id);
    }

    // ---- F-03: the request is not trusted ------------------------------------------

    [DatabaseFact]
    public async Task A_call_under_an_extension_that_is_not_the_agent_s_is_refused()
    {
        // The shared laptop: agent A's queued calls replayed after agent B signs in.
        await data.EnsurePhoneChannelAsync();
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync());
        var request = Call(TestData.NewMobile(), extension: "9001");

        var response = await client.PostAsJsonAsync("/api/communications/calls", request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("extension_not_yours", "the Agent App sets exactly this aside");
        (await data.QueryAsync(db => db.Communications.AnyAsync(c => c.SipCallId == request.SipCallId)))
            .Should().BeFalse();
    }

    [DatabaseFact]
    public async Task A_call_keeps_its_agent_when_its_extension_has_passed_to_somebody_else()
    {
        await data.EnsurePhoneChannelAsync();
        var first = await data.CreateUserAsync();
        var (firstClient, _) = await data.SignInAsync(first);
        var call = Call(TestData.NewMobile(), status: CommunicationStatuses.Missed);
        var logged = await LogAsync(firstClient, call);

        // The supervisor moves the extension to a new agent.
        var second = await data.CreateUserAsync();
        var (secondClient, _) = await data.SignInAsync(second);
        var extension = first.Extension!;
        await data.QueryAsync(async db =>
        {
            var users = await db.Users.Where(u => u.Id == first.Id || u.Id == second.Id).ToListAsync();
            users.Single(u => u.Id == first.Id).Extension = $"8{Random.Shared.Next(1000, 9999)}";
            users.Single(u => u.Id == second.Id).Extension = extension;
            return await db.SaveChangesAsync();
        });

        var response = await secondClient.PostAsJsonAsync("/api/communications/calls",
            call with { Extension = extension, Status = CommunicationStatuses.Answered, AnsweredAt = call.StartedAt.AddSeconds(5) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(response)).Should().Be("not_your_call");
        var stored = await StoredAsync(logged.Id);
        stored.AgentId.Should().Be(first.Id, "an existing call's agent is never changed");
        stored.Status.Should().Be(CommunicationStatuses.Missed);
    }

    [DatabaseFact]
    public async Task After_the_edit_window_a_resend_is_accepted_only_if_it_repeats_what_is_stored()
    {
        // A-42: the default window is the day of the call. The offline queue may
        // deliver a call days late, and resend it after that; a replay that
        // turns yesterday's missed call into a long answered one is refused.
        await data.EnsurePhoneChannelAsync();
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync());
        var started = DateTimeOffset.UtcNow.AddDays(-3);
        var call = Call(TestData.NewMobile(), status: CommunicationStatuses.Missed, started: started);

        var first = await LogAsync(client, call);
        var again = await LogAsync(client, call);
        again.Id.Should().Be(first.Id, "the same call, resent, is accepted and changes nothing");

        var rewrite = await client.PostAsJsonAsync("/api/communications/calls", call with
        {
            Extension = TestData.ExtensionOf(client),
            Status = CommunicationStatuses.Answered,
            AnsweredAt = started.AddSeconds(5),
            EndedAt = started.AddMinutes(40),
        });

        rewrite.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(rewrite)).Should().Be("edit_window_closed");
        var stored = await StoredAsync(first.Id);
        stored.Status.Should().Be(CommunicationStatuses.Missed);
        stored.DurationSec.Should().BeNull();
    }

    [DatabaseFact]
    public async Task A_call_from_the_future_is_refused_and_one_from_last_week_is_not()
    {
        // Dia, 27 Sep: a few minutes ahead is a laptop clock; the past has no
        // limit, because the offline queue can hold a call for days.
        await data.EnsurePhoneChannelAsync();
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync());

        var ahead = await client.PostAsJsonAsync("/api/communications/calls",
            Call(TestData.NewMobile(), started: DateTimeOffset.UtcNow.AddMinutes(20)) with { Extension = TestData.ExtensionOf(client) });
        ahead.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(ahead)).Should().Be("started_in_future");

        await LogAsync(client, Call(TestData.NewMobile(), started: DateTimeOffset.UtcNow.AddMinutes(2)));
        await LogAsync(client, Call(TestData.NewMobile(), started: DateTimeOffset.UtcNow.AddDays(-7)));
    }

    [DatabaseTheory]
    [InlineData(CommunicationStatuses.Answered)]
    [InlineData(CommunicationStatuses.Missed)]
    [InlineData(CommunicationStatuses.Rejected)]
    [InlineData(CommunicationStatuses.Blocked)]
    [InlineData(CommunicationStatuses.NoAnswer)]
    [InlineData(CommunicationStatuses.Failed)]
    public async Task Every_outcome_is_stored_as_reported_against_the_reporting_agent(string status)
    {
        // A-17 wants Blocked in particular: the call was refused before it rang,
        // but it still reaches the supervisor's reports.
        await data.EnsurePhoneChannelAsync();
        var agent = await data.CreateUserAsync();
        var (client, _) = await data.SignInAsync(agent);
        var answered = status == CommunicationStatuses.Answered ? DateTimeOffset.UtcNow.AddSeconds(-30) : (DateTimeOffset?)null;

        var call = await LogAsync(client, Call(TestData.NewMobile(), status: status, answered: answered));

        var stored = await StoredAsync(call.Id);
        stored.Status.Should().Be(status);
        stored.AgentId.Should().Be(agent.Id, "the token decides whose call it is, never the body");
        stored.Kind.Should().Be(CommunicationKinds.Call);
        stored.LaptopId.Should().Be(TestData.LaptopId);
    }

    [DatabaseFact]
    public async Task A_call_never_answered_has_no_duration()
    {
        // Ring time is not talk time. Forty seconds of ringing counted as
        // conversation would sit in every average.
        await data.EnsurePhoneChannelAsync();
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync());
        var started = DateTimeOffset.UtcNow.AddMinutes(-1);

        var call = await LogAsync(client, Call(TestData.NewMobile(),
            status: CommunicationStatuses.Missed, started: started, ended: started.AddSeconds(40)));

        call.DurationSec.Should().BeNull();
    }

    private static LogCallRequest Call(
        string number,
        string? sipCallId = null,
        string extension = "",
        string status = CommunicationStatuses.Answered,
        DateTimeOffset? started = null,
        DateTimeOffset? answered = null,
        DateTimeOffset? ended = null)
    {
        var start = started ?? DateTimeOffset.UtcNow.AddMinutes(-2);
        return new LogCallRequest(
            sipCallId ?? TestData.NewSipCallId(),
            extension,
            Directions.In,
            status,
            number,
            RemoteName: null,
            start,
            answered,
            ended ?? start.AddMinutes(1),
            Queue: null,
            TestData.LaptopId);
    }

    /// <summary>
    /// Posts the call, under the signed-in agent's own extension unless the
    /// request names one (F-03 refuses any other).
    /// </summary>
    private static async Task<CommunicationDto> LogAsync(HttpClient client, LogCallRequest request)
    {
        if (request.Extension.Length == 0)
        {
            request = request with { Extension = TestData.ExtensionOf(client) };
        }

        var response = await client.PostAsJsonAsync("/api/communications/calls", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CommunicationDto>())!;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private Task<Communication> StoredAsync(Guid id) =>
        data.QueryAsync(db => db.Communications.AsNoTracking().SingleAsync(c => c.Id == id));

    private static string ToArabicIndic(string digits) =>
        new(digits.Select(d => (char)('٠' + (d - '0'))).ToArray());
}
