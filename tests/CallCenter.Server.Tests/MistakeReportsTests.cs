using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Mistakes;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The mistakes report (R-23) against a real database: per branch, per agent,
/// over time, and the customers with more than one.
/// </summary>
/// <remarks>
/// Each scenario makes its own two branches and two agents, and reads only its
/// own rows out of each report, so other tests' mistakes cannot change the
/// figures.
/// </remarks>
[Collection(ApiCollection.Name)]
public class MistakeReportsTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    [DatabaseFact]
    public async Task Per_branch_counts_the_branchs_own_and_its_agents_and_adds_the_value()
    {
        var s = await ScenarioAsync();
        await s.RecordAsync(s.BranchA, s.Agent1, 10m);
        await s.RecordAsync(s.BranchA, s.Agent1, 5m);
        await s.RecordAsync(s.BranchA, null, null);
        await s.RecordAsync(s.BranchB, null, 7m);

        var rows = await s.GetAsync<MistakeBranchRowDto>("by-branch", $"from={Today:yyyy-MM-dd}&to={Today:yyyy-MM-dd}");

        var a = rows.Single(r => r.BranchId == s.BranchA);
        a.Mistakes.Should().Be(3);
        a.BranchOwn.Should().Be(1);
        a.ByAgents.Should().Be(2);
        a.Value.Should().Be(15m, "a mistake with no value adds nothing");

        var b = rows.Single(r => r.BranchId == s.BranchB);
        (b.Mistakes, b.BranchOwn, b.ByAgents, b.Value).Should().Be((1, 1, 0, 7m));
    }

    [DatabaseFact]
    public async Task Per_agent_counts_only_the_agents_mistakes_within_the_filters()
    {
        var s = await ScenarioAsync();
        await s.RecordAsync(s.BranchA, s.Agent1, 10m);
        await s.RecordAsync(s.BranchA, s.Agent1, 5m);
        await s.RecordAsync(s.BranchA, null, 100m);
        await s.RecordAsync(s.BranchB, s.Agent2, 3m);

        var rows = await s.GetAsync<MistakeAgentRowDto>("by-agent", $"branchId={s.BranchA}");

        rows.Should().ContainSingle("the branch's own mistake names no agent, and agent 2's was at the other branch");
        rows[0].AgentId.Should().Be(s.Agent1);
        rows[0].Mistakes.Should().Be(2);
        rows[0].Value.Should().Be(15m);

        // Both branches at once: either matches (Dia, 2 Oct 2026).
        var both = await s.GetAsync<MistakeAgentRowDto>("by-agent", $"branchId={s.BranchA}&branchId={s.BranchB}");
        both.Select(r => (r.AgentId, r.Mistakes)).Should().BeEquivalentTo([(s.Agent1, 2), (s.Agent2, 1)]);
    }

    [DatabaseFact]
    public async Task Over_time_groups_by_day_and_by_month()
    {
        var s = await ScenarioAsync();
        var earlier = Today.AddDays(-40);
        await s.RecordAsync(s.BranchA, s.Agent1, 4m);
        await s.RecordAsync(s.BranchA, null, null);
        await s.RecordAsync(s.BranchA, s.Agent1, 6m, occurredOn: earlier);

        var days = await s.GetAsync<MistakeTrendPointDto>("trend", $"branchId={s.BranchA}&groupBy=day");
        days.Select(d => d.Bucket).Should().Equal($"{earlier:yyyy-MM-dd}", $"{Today:yyyy-MM-dd}");
        var today = days.Last();
        (today.Mistakes, today.BranchOwn, today.ByAgents, today.Value).Should().Be((2, 1, 1, 4m));

        var months = await s.GetAsync<MistakeTrendPointDto>("trend", $"branchId={s.BranchA}&groupBy=month");
        months.Select(m => m.Bucket).Should().Equal($"{earlier:yyyy-MM}", $"{Today:yyyy-MM}");
    }

    [DatabaseFact]
    public async Task Repeat_customers_join_a_saved_customers_numbers_and_leave_out_a_single_mistake()
    {
        var s = await ScenarioAsync();
        var stranger = TestData.NewMobile();
        var once = TestData.NewMobile();

        // One saved customer, typed two ways: one person (A-13).
        await s.RecordAsync(s.BranchA, null, 5m, s.Mobile);
        await s.RecordAsync(s.BranchA, s.Agent1, 8m, "+970" + s.Mobile[1..], occurredOn: Today.AddDays(-1));
        await s.RecordAsync(s.BranchA, null, null, stranger);
        await s.RecordAsync(s.BranchA, null, null, stranger);
        await s.RecordAsync(s.BranchA, null, null, once);

        var rows = await s.GetAsync<MistakeCustomerRowDto>("repeat-customers", $"branchId={s.BranchA}");

        rows.Should().HaveCount(2, "the number with one mistake is not a repeat");

        var saved = rows.Single(r => r.ContactId == s.ContactId);
        saved.Customer.Should().Be("سارة الحلبي");
        saved.Mistakes.Should().Be(2);
        saved.Value.Should().Be(13m);
        saved.Last.Should().Be(Today);
        saved.Number.Should().Be(s.Mobile, "the number as typed on the latest mistake");

        var unknown = rows.Single(r => r.ContactId == null);
        unknown.Number.Should().Be(stranger);
        unknown.Mistakes.Should().Be(2);
    }

    [Theory]
    [InlineData("by-branch")]
    [InlineData("by-agent")]
    [InlineData("trend")]
    [InlineData("repeat-customers")]
    public async Task An_agent_cannot_read_the_report(string report)
    {
        var tokens = new TokenService(
            Options.Create(new JwtOptions { SigningKey = CallCenterApiFactory.SigningKey }), TimeProvider.System);
        var (token, _) = tokens.Issue(
            new User { Id = Guid.NewGuid(), Login = "agent", DisplayName = "Agent", Role = UserRoles.Agent, PasswordHash = "unused" },
            sessionId: null);

        var client = factory.CreateClient();
        (await client.GetAsync($"/api/mistakes/reports/{report}")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await client.GetAsync($"/api/mistakes/reports/{report}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<Scenario> ScenarioAsync()
    {
        var agent1 = await data.CreateUserAsync();
        var agent2 = await data.CreateUserAsync();
        var (client, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        var mobile = TestData.NewMobile();
        var contact = await data.CreateContactAsync("سارة الحلبي", mobile);

        var (a, b) = await data.QueryAsync(async db =>
        {
            var first = new Branch { Name = $"Test branch {Guid.NewGuid().ToString("N")[..8]}" };
            var second = new Branch { Name = $"Test branch {Guid.NewGuid().ToString("N")[..8]}" };
            db.Branches.AddRange(first, second);
            await db.SaveChangesAsync();
            return (first.Id, second.Id);
        });

        return new Scenario(client, a, b, agent1.Id, agent2.Id, contact.Id, mobile);
    }

    private sealed record Scenario(
        HttpClient Supervisor, Guid BranchA, Guid BranchB, Guid Agent1, Guid Agent2, Guid ContactId, string Mobile)
    {
        public async Task RecordAsync(Guid branch, Guid? agent, decimal? value, string? number = null, DateOnly? occurredOn = null)
        {
            var response = await Supervisor.PostAsJsonAsync("/api/mistakes", new UpsertMistakeRequest(
                occurredOn ?? Today, branch,
                agent is null ? MistakeResponsibilities.Branch : MistakeResponsibilities.Agent,
                agent, value, number, "Something went wrong"));

            response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        }

        public async Task<List<T>> GetAsync<T>(string report, string query) =>
            (await Supervisor.GetFromJsonAsync<List<T>>($"/api/mistakes/reports/{report}?{query}"))!;
    }
}
