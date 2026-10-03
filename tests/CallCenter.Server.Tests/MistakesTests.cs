using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Mistakes;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Mistakes;
using CallCenter.Shared.Text;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The mistakes made by the branches and the agents (S-65), against a real
/// database: who is responsible, the customer found by number, the search and
/// its totals, corrections, removal and the export.
/// </summary>
/// <remarks>
/// Each scenario makes its own branches and agents, and every search is
/// narrowed to one of its branches, so rows earlier runs left cannot leak in.
/// </remarks>
[Collection(ApiCollection.Name)]
public class MistakesTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    // ---- who is responsible --------------------------------------------------

    [DatabaseFact]
    public async Task An_agents_mistake_names_the_agent_and_the_branch_and_finds_the_customer_by_number()
    {
        var s = await ScenarioAsync();

        // Saved as 059…, typed as +970 59…: one person (A-13).
        var typed = "+970" + s.CustomerMobile[1..];
        var response = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.BranchId, MistakeResponsibilities.Agent, s.AgentId, 35.5m, typed, "Wrong burger sent"));

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var saved = (await response.Content.ReadFromJsonAsync<MistakeDto>())!;

        saved.BranchId.Should().Be(s.BranchId);
        saved.Responsible.Should().Be(MistakeResponsibilities.Agent);
        saved.AgentId.Should().Be(s.AgentId);
        saved.AgentDisplayName.Should().Be(s.AgentName);
        saved.Value.Should().Be(35.5m);
        saved.ContactId.Should().Be(s.ContactId);
        saved.ContactName.Should().Be("سارة الحلبي");
        saved.CustomerNumber.Should().Be(typed, "the number is kept as the supervisor typed it");
        saved.Notes.Should().Be("Wrong burger sent");
        saved.CreatedByDisplayName.Should().NotBeNullOrEmpty();
    }

    [DatabaseFact]
    public async Task A_branchs_mistake_names_no_agent()
    {
        var s = await ScenarioAsync();

        var saved = await s.CreateAsync(MistakeResponsibilities.Branch, agentId: null, notes: "Opened late");

        saved.Responsible.Should().Be(MistakeResponsibilities.Branch);
        saved.AgentId.Should().BeNull();
        saved.Value.Should().BeNull("a mistake may have no value");
        saved.ContactId.Should().BeNull("a customer is optional");
    }

    [DatabaseFact]
    public async Task A_branchs_mistake_with_an_agent_and_an_agents_mistake_without_one_are_refused()
    {
        var s = await ScenarioAsync();

        var withAgent = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.BranchId, MistakeResponsibilities.Branch, s.AgentId, null, null, "x"));
        withAgent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(withAgent)).Should().Be("agent_not_allowed");

        var withoutAgent = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.BranchId, MistakeResponsibilities.Agent, null, null, null, "x"));
        withoutAgent.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Code(withoutAgent)).Should().Be("agent_required");

        var supervisorAsAgent = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.BranchId, MistakeResponsibilities.Agent, s.SupervisorId, null, null, "x"));
        (await Code(supervisorAsAgent)).Should().Be("unknown_agent", "only an agent's account can be responsible");

        var nobody = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.BranchId, "Kitchen", null, null, null, "x"));
        (await Code(nobody)).Should().Be("bad_responsible");
    }

    // ---- the customer --------------------------------------------------------

    [DatabaseFact]
    public async Task A_number_nobody_has_on_file_is_kept_as_typed()
    {
        var s = await ScenarioAsync();
        var stranger = TestData.NewMobile();

        var saved = await s.CreateAsync(MistakeResponsibilities.Branch, null, customerNumber: stranger);

        saved.ContactId.Should().BeNull();
        saved.ContactName.Should().BeNull();
        saved.CustomerNumber.Should().Be(stranger);
    }

    [DatabaseFact]
    public async Task A_number_with_no_digits_is_refused()
    {
        var s = await ScenarioAsync();

        var response = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.BranchId, MistakeResponsibilities.Branch, null, null, "abc", "x"));

        (await Code(response)).Should().Be("bad_number");
    }

    // ---- other refusals ------------------------------------------------------

    [DatabaseFact]
    public async Task Blank_notes_and_a_date_after_today_are_refused()
    {
        var s = await ScenarioAsync();

        var blank = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.BranchId, MistakeResponsibilities.Branch, null, null, null, "   "));
        blank.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var tomorrow = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today.AddDays(1), s.BranchId, MistakeResponsibilities.Branch, null, null, null, "x"));
        (await Code(tomorrow)).Should().Be("future_date");

        var negative = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.BranchId, MistakeResponsibilities.Branch, null, -1m, null, "x"));
        negative.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [DatabaseFact]
    public async Task A_disabled_branch_or_agent_is_refused_for_a_new_mistake_but_kept_on_an_old_one()
    {
        var s = await ScenarioAsync();
        var old = await s.CreateAsync(MistakeResponsibilities.Agent, s.AgentId, notes: "before");

        await data.QueryAsync(async db =>
        {
            await db.Branches.Where(b => b.Id == s.BranchId).ExecuteUpdateAsync(b => b.SetProperty(x => x.IsActive, false));
            await db.Users.Where(u => u.Id == s.AgentId).ExecuteUpdateAsync(u => u.SetProperty(x => x.IsActive, false));
            return 0;
        });

        var fresh = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.BranchId, MistakeResponsibilities.Branch, null, null, null, "x"));
        (await Code(fresh)).Should().Be("branch_inactive");

        var freshAgent = await s.Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
            Today, s.OtherBranchId, MistakeResponsibilities.Agent, s.AgentId, null, null, "x"));
        (await Code(freshAgent)).Should().Be("agent_inactive");

        // Correcting the notes leaves the branch and the agent alone.
        var corrected = await s.Supervisor.PutAsJsonAsync($"/api/mistakes/{old.Id}", new UpsertMistakeRequest(
            old.OccurredOn, s.BranchId, MistakeResponsibilities.Agent, s.AgentId, null, null, "after"));
        corrected.StatusCode.Should().Be(HttpStatusCode.OK, await corrected.Content.ReadAsStringAsync());
        (await corrected.Content.ReadFromJsonAsync<MistakeDto>())!.Notes.Should().Be("after");
    }

    // ---- the search ----------------------------------------------------------

    [DatabaseFact]
    public async Task The_search_filters_on_the_server_and_totals_every_match()
    {
        var s = await ScenarioAsync();
        await s.CreateAsync(MistakeResponsibilities.Agent, s.AgentId, value: 10m);
        await s.CreateAsync(MistakeResponsibilities.Agent, s.AgentId, value: 15.25m, occurredOn: Today.AddDays(-3));
        await s.CreateAsync(MistakeResponsibilities.Branch, null);
        await s.CreateAsync(MistakeResponsibilities.Branch, null, value: 99m, branchId: s.OtherBranchId);

        var all = await s.SearchAsync($"branchId={s.BranchId}");
        all.Total.Should().Be(3, "the other branch's mistake is left out");
        all.TotalValue.Should().Be(25.25m, "a mistake with no value counts as nothing");
        all.Rows.First().OccurredOn.Should().Be(Today, "newest day first");

        var agents = await s.SearchAsync($"branchId={s.BranchId}&responsible=Agent");
        agents.Total.Should().Be(2);

        var byAgent = await s.SearchAsync($"agentId={s.AgentId}");
        byAgent.Total.Should().Be(2);

        var today = await s.SearchAsync($"branchId={s.BranchId}&from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");
        today.Total.Should().Be(2, "the one three days ago is outside the days asked for");

        var paged = await s.SearchAsync($"branchId={s.BranchId}", "&page=2&pageSize=2");
        paged.Rows.Should().HaveCount(1);
        paged.Total.Should().Be(3, "the count is of every match, not the page");
    }

    [DatabaseFact]
    public async Task The_search_finds_a_mistake_by_the_customers_number_name_or_the_notes()
    {
        var s = await ScenarioAsync();

        // TestData writes the name alone; the apps write its matching form too.
        await data.QueryAsync(db => db.Contacts.Where(c => c.Id == s.ContactId)
            .ExecuteUpdateAsync(c => c.SetProperty(x => x.NameNormalised, NameNormalizer.Normalize("سارة الحلبي"))));

        await s.CreateAsync(MistakeResponsibilities.Branch, null, customerNumber: s.CustomerMobile, notes: "Cold fries");
        await s.CreateAsync(MistakeResponsibilities.Branch, null, notes: "Late delivery");

        (await s.SearchAsync($"branchId={s.BranchId}&q=" + Uri.EscapeDataString("+970" + s.CustomerMobile[1..]))).Total
            .Should().Be(1, "a number is matched on its last nine digits, as the call search matches one");
        (await s.SearchAsync($"branchId={s.BranchId}&q=" + Uri.EscapeDataString("ساره"))).Total
            .Should().Be(1, "the name is matched as contact names are, ة and ه alike");
        (await s.SearchAsync($"branchId={s.BranchId}&q=late")).Total.Should().Be(1);
    }

    [DatabaseFact]
    public async Task Compensated_is_saved_corrected_and_filtered_on()
    {
        var s = await ScenarioAsync();
        var madeGood = await s.CreateAsync(MistakeResponsibilities.Agent, s.AgentId, value: 20m, compensated: true);
        var open = await s.CreateAsync(MistakeResponsibilities.Branch, null, value: 5m);

        madeGood.Compensated.Should().BeTrue();
        open.Compensated.Should().BeFalse("a mistake is not compensated until the supervisor ticks it");

        var yes = await s.SearchAsync($"branchId={s.BranchId}&compensated=true");
        yes.Rows.Select(r => r.Id).Should().Equal(madeGood.Id);
        yes.TotalValue.Should().Be(20m);

        var no = await s.SearchAsync($"branchId={s.BranchId}&compensated=false");
        no.Rows.Select(r => r.Id).Should().Equal(open.Id);

        (await s.SearchAsync($"branchId={s.BranchId}")).Total.Should().Be(2, "left out, the filter is off");

        // Compensated later: the correction ticks it.
        var corrected = await s.Supervisor.PutAsJsonAsync($"/api/mistakes/{open.Id}", new UpsertMistakeRequest(
            open.OccurredOn, s.BranchId, MistakeResponsibilities.Branch, null, 5m, null, open.Notes, Compensated: true));
        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        (await corrected.Content.ReadFromJsonAsync<MistakeDto>())!.Compensated.Should().BeTrue();

        (await s.SearchAsync($"branchId={s.BranchId}&compensated=false")).Total.Should().Be(0);
    }

    // ---- corrections and removal ---------------------------------------------

    [DatabaseFact]
    public async Task A_mistake_can_be_corrected_and_removed_and_the_audit_log_keeps_both()
    {
        var s = await ScenarioAsync();
        var saved = await s.CreateAsync(MistakeResponsibilities.Agent, s.AgentId, notes: "Wrong address");

        // Put down to the branch instead: the agent goes.
        var corrected = await s.Supervisor.PutAsJsonAsync($"/api/mistakes/{saved.Id}", new UpsertMistakeRequest(
            saved.OccurredOn, s.BranchId, MistakeResponsibilities.Branch, null, 20m, s.CustomerMobile, "Wrong address, branch's"));
        corrected.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = (await corrected.Content.ReadFromJsonAsync<MistakeDto>())!;
        after.AgentId.Should().BeNull();
        after.Value.Should().Be(20m);
        after.ContactId.Should().Be(s.ContactId);

        var removed = await s.Supervisor.DeleteAsync($"/api/mistakes/{saved.Id}");
        removed.StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await s.Supervisor.DeleteAsync($"/api/mistakes/{saved.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await s.SearchAsync($"branchId={s.BranchId}")).Total.Should().Be(0);

        var actions = await data.QueryAsync(db => db.AuditLog
            .Where(a => a.Entity == MistakesService.AuditEntity && a.EntityId == saved.Id.ToString())
            .OrderBy(a => a.Id)
            .Select(a => a.Action)
            .ToListAsync());
        actions.Should().Equal("create", "update", "delete");
    }

    // ---- the export ----------------------------------------------------------

    [DatabaseFact]
    public async Task The_export_has_every_match_with_Arabic_headings_and_the_number_as_typed()
    {
        var s = await ScenarioAsync();
        await s.CreateAsync(MistakeResponsibilities.Agent, s.AgentId, value: 12m, customerNumber: s.CustomerMobile, notes: "=cmd",
            compensated: true);
        await s.CreateAsync(MistakeResponsibilities.Branch, null);

        var response = await s.Supervisor.GetAsync($"/api/mistakes/export?branchId={s.BranchId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Take(3).Should().Equal(new byte[] { 0xEF, 0xBB, 0xBF }, "Excel needs the byte-order mark to read the Arabic");

        var lines = Encoding.UTF8.GetString(bytes).TrimStart('﻿').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(3, "a heading and both mistakes");
        lines[0].Should().StartWith("التاريخ,الفرع,المسؤول");
        lines.Should().Contain(l => l.Contains($"\"=\"\"{s.CustomerMobile}\"\"\""), "the number keeps its leading 0");
        lines.Should().Contain(l => l.Contains("'=cmd"), "nothing runs as a formula (M-S02)");
        lines[0].Should().Contain(",تم التعويض,");
        lines.Should().Contain(l => l.Contains(",نعم,")).And.Contain(l => l.Contains(",لا,"), "compensated is a word, not a tick");

        var english = await s.Supervisor.GetStringAsync($"/api/mistakes/export?branchId={s.BranchId}&lang=en");
        english.TrimStart('﻿').Should().StartWith("Date,Branch,Responsible");
        english.Should().Contain(",Compensated,").And.Contain(",Yes,");
    }

    // ---- who may -------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/mistakes")]
    [InlineData("GET", "/api/mistakes/export")]
    [InlineData("POST", "/api/mistakes")]
    [InlineData("PUT", "/api/mistakes/11111111-1111-1111-1111-111111111111")]
    [InlineData("DELETE", "/api/mistakes/11111111-1111-1111-1111-111111111111")]
    public async Task An_agent_can_neither_read_nor_record_mistakes(string method, string path)
    {
        var tokens = new TokenService(
            Options.Create(new JwtOptions { SigningKey = CallCenterApiFactory.SigningKey }), TimeProvider.System);
        var (token, _) = tokens.Issue(
            new User { Id = Guid.NewGuid(), Login = "agent", DisplayName = "Agent", Role = UserRoles.Agent, PasswordHash = "unused" },
            sessionId: null);

        var client = factory.CreateClient();
        var request = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };

        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "no token, no entry");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var again = new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) };
        (await client.SendAsync(again)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ---- helpers -------------------------------------------------------------

    private static async Task<string?> Code(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private async Task<Scenario> ScenarioAsync()
    {
        var agent = await data.CreateUserAsync();
        var supervisor = await data.CreateUserAsync(UserRoles.Supervisor);
        var (client, _) = await data.SignInAsync(supervisor);

        var mobile = TestData.NewMobile();
        var contact = await data.CreateContactAsync("سارة الحلبي", mobile);

        var (branch, other) = await data.QueryAsync(async db =>
        {
            var a = new Branch { Name = $"Test branch {Guid.NewGuid().ToString("N")[..8]}" };
            var b = new Branch { Name = $"Test branch {Guid.NewGuid().ToString("N")[..8]}" };
            db.Branches.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });

        return new Scenario(client, branch, other, agent.Id, agent.DisplayName, supervisor.Id, contact.Id, mobile);
    }

    private sealed record Scenario(
        HttpClient Supervisor,
        Guid BranchId,
        Guid OtherBranchId,
        Guid AgentId,
        string AgentName,
        Guid SupervisorId,
        Guid ContactId,
        string CustomerMobile)
    {
        public async Task<MistakeDto> CreateAsync(
            string responsible,
            Guid? agentId,
            decimal? value = null,
            string? customerNumber = null,
            string notes = "Something went wrong",
            DateOnly? occurredOn = null,
            Guid? branchId = null,
            bool compensated = false)
        {
            var response = await Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
                occurredOn ?? Today, branchId ?? BranchId, responsible, agentId, value, customerNumber, notes, compensated));

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            return (await response.Content.ReadFromJsonAsync<MistakeDto>())!;
        }

        public async Task<MistakePageDto> SearchAsync(string filters, string paging = "") =>
            (await Supervisor.GetFromJsonAsync<MistakePageDto>($"/api/mistakes?{filters}{paging}"))!;
    }
}
