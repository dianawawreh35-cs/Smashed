using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using CallCenter.Server.Features.Pbx;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using CallCenter.Shared.Contracts.Pbx;
using CallCenter.Shared.Phone;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The abandoned calls (S-55): the PBX's Calls Detail CSV read, each call saved
/// once however often its day is downloaded, the Agent App's rings joined to
/// it, and the reports counting the customer once.
/// </summary>
/// <remarks>
/// No PBX here. The CSV is written as the real one was on 26 Sep, and handed to
/// the import directly; the test host has no PBX login, so its timer never
/// makes a request. Each database test uses a random day in 2010-2016, which no
/// other test writes to, and the queue name <c>test-queue</c>, which is how the
/// sweep finds the rows.
/// </remarks>
[Collection(ApiCollection.Name)]
public class AbandonedCallImportTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    private const string Header =
        "\"No. Agent\",\"Agent\",\"Start Time\",\"End Time\",\"Duration\",\"Duration Wait\",\"Queue\",\"Type\",\"Phone\",\"Transfer\",\"Status\",";

    private const string Queue = "test-queue";

    // ---- reading the report -----------------------------------------------------

    [Fact]
    public void Only_incoming_abandoned_rows_are_read_with_their_wait_and_hang_up()
    {
        var csv = string.Join("\n",
            Header,
            "\"2001\",\"Diaa\",\"2026-09-24 23:29:00\",\"2026-09-24 23:29:06\",\"00:00:07\",\"00:00:04\",\"002\",\"Incoming\",\"0569000001\",\"\",\"Success\",",
            "\"\",\"\",\"\",\"2026-09-22 16:49:04\",\"-\",\"00:01:55\",\"002\",\"Incoming\",\"0569000002\",\"\",\"Abandoned\",",
            "\"\",\"\",\"\",\"2026-09-22 16:50:00\",\"-\",\"00:00:10\",\"002\",\"Outgoing\",\"0569000003\",\"\",\"Abandoned\",",
            "\"\",\"\",\"\",\"\",\"-\",\"00:00:05\",\"002\",\"Incoming\",\"0569000004\",\"\",\"Abandoned\",",
            "");

        var parsed = CallsDetailCsv.Parse(csv);

        parsed.Calls.Should().Be(4);
        var row = parsed.Abandoned.Should().ContainSingle().Subject;
        row.Phone.Should().Be("0569000002");
        row.WaitSec.Should().Be(115);
        row.EndedAt.Should().Be(new DateTime(2026, 9, 22, 16, 49, 4));
        row.QueuedAt.Should().Be(new DateTime(2026, 9, 22, 16, 47, 9));
        row.Key.Should().Be("issabel:2026-09-22 16:49:04|0569000002|002");
    }

    [Fact]
    public void A_report_in_another_language_is_refused_with_the_reason()
    {
        var act = () => CallsDetailCsv.Parse("\"No. Agente\",\"Agente\",\"Inicio\",\"Fin\",\"Estado\"\n\"\",\"\",\"\",\"\",\"Abandonada\"");

        act.Should().Throw<PbxImportException>().WithMessage("*English*");
    }

    [Fact]
    public void The_login_page_is_told_apart_from_the_report()
    {
        IssabelCallsClient.NeedsLogin("<html><form><input name=\"input_pass\"></form></html>").Should().BeTrue();
        // What the export link answers without a session, word for word (26 Sep).
        IssabelCallsClient.NeedsLogin(
            "{\"error\":\"Your session has expired. If you want to do a login please press the button 'Accept'.\",\"message\":null,\"statusResponse\":\"ERROR_SESSION\"}")
            .Should().BeTrue();
        IssabelCallsClient.NeedsLogin(Header + "\n").Should().BeFalse();
    }

    [Fact]
    public void The_export_link_is_the_one_the_PBX_page_itself_uses()
    {
        var uri = IssabelCallsClient.ExportUri(new Uri("https://192.168.0.27"), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 21));

        uri.AbsoluteUri.Should().Be(
            "https://192.168.0.27/index.php?menu=calls_detail&date_start=01%20Sep%202026&date_end=21%20Sep%202026"
            + "&calltype=&agent=&queue=&phone=&id_campaign_out=&id_campaign_in=&exportcsv=yes&rawmode=yes");
    }

    // ---- saving -----------------------------------------------------------------

    [DatabaseFact]
    public async Task Downloading_the_same_day_again_adds_nothing()
    {
        await data.EnsurePhoneChannelAsync();
        var day = RandomDay();
        var number = TestData.NewMobile();
        var parsed = Parse(Abandoned(day.AddHours(12), 30, number), Abandoned(day.AddHours(12).AddSeconds(19), 5, number));

        var first = await ApplyAsync(parsed, day);
        var second = await ApplyAsync(parsed, day);

        first.Added.Should().Be(2, "two attempts 19 seconds apart are two calls");
        second.Added.Should().Be(0);
        (await data.QueryAsync(db => db.Communications.CountAsync(c => c.RemoteNormalised == PhoneNormalizer.Normalize(number)))).Should().Be(2);

        var call = await data.QueryAsync(db => db.Communications.AsNoTracking()
            .Where(c => c.RemoteNormalised == PhoneNormalizer.Normalize(number)).OrderBy(c => c.StartedAt).FirstAsync());
        call.Status.Should().Be(CommunicationStatuses.Abandoned);
        call.Direction.Should().Be(Directions.In);
        call.AgentId.Should().BeNull();
        call.Source.Should().Be(CommunicationSources.Cdr);
        call.WaitSec.Should().Be(30);
        call.EndedAt.Should().Be(AbandonedCallImport.Utc(day.AddHours(12)));
        call.StartedAt.Should().Be(AbandonedCallImport.Utc(day.AddHours(12).AddSeconds(-30)));
    }

    // ---- the rings, and the reports -----------------------------------------------

    [DatabaseFact]
    public async Task The_rings_of_an_abandoned_call_are_joined_to_it_and_the_customer_counts_once()
    {
        await data.EnsurePhoneChannelAsync();
        var user = await data.CreateUserAsync();
        var (agent, _) = await data.SignInAsync(user);
        var agentId = user.Id;
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var day = RandomDay();
        var number = TestData.NewMobile();
        var hangUp = day.AddHours(18);

        // The caller waited 1:55; the queue rang the agent three times, one of
        // them rejected. A fourth untaken ring ten minutes earlier is its own call.
        var rings = new[]
        {
            await RingAsync(agent, number, hangUp.AddSeconds(-116), CommunicationStatuses.Missed),
            await RingAsync(agent, number, hangUp.AddSeconds(-80), CommunicationStatuses.Rejected),
            await RingAsync(agent, number, hangUp.AddSeconds(-20), CommunicationStatuses.Missed),
        };
        var earlier = await RingAsync(agent, number, hangUp.AddMinutes(-10), CommunicationStatuses.Missed);

        var result = await ApplyAsync(Parse(Abandoned(hangUp, 115, number)), day);

        result.RingsLinked.Should().Be(3);
        var callId = await data.QueryAsync(db => db.Communications
            .Where(c => c.RemoteNormalised == PhoneNormalizer.Normalize(number) && c.Status == CommunicationStatuses.Abandoned).Select(c => c.Id).SingleAsync());
        foreach (var ring in rings)
        {
            (await AbandonedCallOfAsync(ring)).Should().Be(callId);
        }
        (await AbandonedCallOfAsync(earlier)).Should().BeNull();

        // Downloading again joins nothing twice.
        (await ApplyAsync(Parse(Abandoned(hangUp, 115, number)), day)).RingsLinked.Should().Be(0);

        var period = $"from={Instant(At(day))}&to={Instant(At(day.AddDays(1)))}";

        // R-11: one abandoned call, one missed call (the earlier ring), never the joined rings.
        var missed = (await supervisor.GetFromJsonAsync<List<MissedRowDto>>($"/api/reports/calls/missed?{period}"))!.Single();
        missed.Inbound.Should().Be(2);
        missed.Missed.Should().Be(1);
        missed.Rejected.Should().Be(0);
        missed.Abandoned.Should().Be(1);
        missed.Total.Should().Be(2);

        // R-01 agrees.
        var summary = (await supervisor.GetFromJsonAsync<List<CallSummaryRowDto>>($"/api/reports/calls/summary?{period}"))!.Single();
        summary.Inbound.Should().Be(2);
        summary.Abandoned.Should().Be(1);
        summary.Missed.Should().Be(1);

        // R-15 still counts every ring on the agent's phone.
        var agents = (await supervisor.GetFromJsonAsync<List<AgentProductivityRowDto>>($"/api/reports/calls/agents?{period}"))!;
        agents.Single(a => a.AgentId == agentId).Missed.Should().Be(4);

        // R-20: the call, its wait and its rings; nobody has rung back yet.
        var list = (await supervisor.GetFromJsonAsync<List<AbandonedCallRowDto>>($"/api/reports/calls/abandoned/list?{period}"))!;
        var row = list.Should().ContainSingle().Subject;
        row.WaitSec.Should().Be(115);
        row.Rings.Should().Be(3);
        row.CalledBackAt.Should().BeNull();

        var figures = (await supervisor.GetFromJsonAsync<List<AbandonedRowDto>>($"/api/reports/calls/abandoned?{period}"))!.Single();
        figures.Inbound.Should().Be(2);
        figures.Abandoned.Should().Be(1);
        figures.Rate.Should().Be(50m);
        figures.AverageWaitSec.Should().Be(115);
        figures.CalledBack.Should().Be(0);
    }

    [DatabaseFact]
    public async Task A_call_back_after_the_hang_up_shows_on_the_abandoned_list()
    {
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var day = RandomDay();
        var number = TestData.NewMobile();
        var hangUp = day.AddHours(10);

        await ApplyAsync(Parse(Abandoned(hangUp, 40, number)), day);
        await CallAsync(agent, number, hangUp.AddMinutes(-30), Directions.Out, CommunicationStatuses.Answered);
        await CallAsync(agent, number, hangUp.AddMinutes(7), Directions.Out, CommunicationStatuses.NoAnswer);

        var period = $"from={Instant(At(day))}&to={Instant(At(day.AddDays(1)))}";
        var row = (await supervisor.GetFromJsonAsync<List<AbandonedCallRowDto>>($"/api/reports/calls/abandoned/list?{period}"))!.Single();

        row.CalledBackAt.Should().Be(At(hangUp.AddMinutes(7)), "the first call after the hang-up, answered or not; the one before is not a call back");
        row.MinutesToCallBack.Should().Be(7);
        row.CalledBackBy.Should().NotBeNullOrEmpty();
    }

    [DatabaseFact]
    public async Task A_caller_every_laptop_refused_is_saved_as_blocked_not_abandoned()
    {
        await data.EnsurePhoneChannelAsync();
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var day = RandomDay();
        var number = TestData.NewMobile();
        var hangUp = day.AddHours(9);

        await RingAsync(agent, number, hangUp.AddSeconds(-80), CommunicationStatuses.Blocked);
        await RingAsync(agent, number, hangUp.AddSeconds(-40), CommunicationStatuses.Blocked);

        await ApplyAsync(Parse(Abandoned(hangUp, 86, number)), day);

        var status = await data.QueryAsync(db => db.Communications
            .Where(c => c.RemoteNormalised == PhoneNormalizer.Normalize(number) && c.Source == CommunicationSources.Cdr).Select(c => c.Status).SingleAsync());
        status.Should().Be(CommunicationStatuses.Blocked);
    }

    // ---- the settings -----------------------------------------------------------

    [DatabaseFact]
    public async Task Settings_with_a_bad_address_or_interval_are_refused_and_nothing_is_saved()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var before = (await supervisor.GetFromJsonAsync<AbandonedImportDto>("/api/pbx/abandoned-import"))!;

        var response = await supervisor.PutAsJsonAsync("/api/pbx/abandoned-import",
            new UpdateAbandonedImportRequest("not a url", "someone", "secret", 0));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("url").And.Contain("intervalMinutes");
        (await supervisor.GetFromJsonAsync<AbandonedImportDto>("/api/pbx/abandoned-import")).Should().Be(before);
    }

    [DatabaseFact]
    public async Task An_agent_cannot_see_the_import_settings()
    {
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());

        (await agent.GetAsync("/api/pbx/abandoned-import")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- helpers ----------------------------------------------------------------

    /// <summary>A local midnight in 2010-2016, different every run.</summary>
    private static DateTime RandomDay() => new DateTime(2010, 1, 1).AddDays(Random.Shared.Next(0, 2500));

    private static DateTimeOffset At(DateTime local) => AbandonedCallImport.Utc(local);

    private static string Instant(DateTimeOffset at) => Uri.EscapeDataString(at.ToString("o", CultureInfo.InvariantCulture));

    private static string Abandoned(DateTime hangUp, int waitSec, string number) =>
        $"\"\",\"\",\"\",\"{hangUp:yyyy-MM-dd HH:mm:ss}\",\"-\",\"{TimeSpan.FromSeconds(waitSec):hh\\:mm\\:ss}\",\"{Queue}\",\"Incoming\",\"{number}\",\"\",\"Abandoned\",";

    private static CallsDetailCsv.Parsed Parse(params string[] rows) =>
        CallsDetailCsv.Parse(string.Join("\n", [Header, .. rows, ""]));

    private async Task<(int Added, int RingsLinked)> ApplyAsync(CallsDetailCsv.Parsed parsed, DateTime day)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var import = scope.ServiceProvider.GetRequiredService<AbandonedCallImport>();
        var date = DateOnly.FromDateTime(day);
        return await import.ApplyAsync(parsed, date, date);
    }

    private static Task<Guid> RingAsync(HttpClient agent, string number, DateTime at, string status) =>
        CallAsync(agent, number, at, Directions.In, status);

    private static async Task<Guid> CallAsync(HttpClient agent, string number, DateTime at, string direction, string status)
    {
        var started = At(at);
        var answered = status == CommunicationStatuses.Answered ? started.AddSeconds(3) : (DateTimeOffset?)null;
        var response = await agent.PostAsJsonAsync("/api/communications/calls", new LogCallRequest(
            TestData.NewSipCallId(), "9000", direction, status, number,
            RemoteName: null, started, answered, started.AddSeconds(15), Queue: null, TestData.LaptopId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<CommunicationDto>())!.Id;
    }

    private Task<Guid?> AbandonedCallOfAsync(Guid ringId) =>
        data.QueryAsync(db => db.Communications.Where(c => c.Id == ringId).Select(c => c.AbandonedCallId).SingleAsync());
}
