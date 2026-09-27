using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Classifications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Who closes a complaint (R-17): a supervisor, and nobody's later save opens
/// it again by accident. Against a real database.
/// </summary>
/// <remarks>
/// F-05 of the 27 Sep review. The Agent App never offers the Resolved tick and
/// always sends null; the server copied any value that differed from the
/// stored one, so an agent fixing a typo in the notes cleared Resolved,
/// ResolvedAt and ResolvedBy on a complaint the supervisor had closed.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ComplaintResolvedTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task An_agent_s_later_save_leaves_a_resolved_complaint_resolved()
    {
        var s = await ScenarioAsync();

        (await s.Supervisor.PutAsJsonAsync($"/api/classifications/{s.CallId}", s.Save(resolved: true, notes: "cold fries")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // What the Agent App sends: Resolved null, a note corrected.
        var agentSave = await s.Agent.PutAsJsonAsync($"/api/classifications/{s.CallId}", s.Save(resolved: null, notes: "cold fries, refunded"));
        agentSave.StatusCode.Should().Be(HttpStatusCode.OK, await agentSave.Content.ReadAsStringAsync());

        var row = await RowAsync(s.CallId);
        row.Notes.Should().Be("cold fries, refunded");
        row.Resolved.Should().BeTrue();
        row.ResolvedBy.Should().Be(s.SupervisorId);
        row.ResolvedAt.Should().NotBeNull();
    }

    [DatabaseFact]
    public async Task An_agent_cannot_resolve_or_reopen_a_complaint()
    {
        var s = await ScenarioAsync();

        (await s.Agent.PutAsJsonAsync($"/api/classifications/{s.CallId}", s.Save(resolved: true)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await RowAsync(s.CallId)).Resolved.Should().BeNull("only a supervisor closes a complaint");

        (await s.Supervisor.PutAsJsonAsync($"/api/classifications/{s.CallId}", s.Save(resolved: true)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await s.Agent.PutAsJsonAsync($"/api/classifications/{s.CallId}", s.Save(resolved: false)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await RowAsync(s.CallId)).Resolved.Should().BeTrue("an agent's false is ignored as well as their null");
    }

    [DatabaseFact]
    public async Task A_supervisor_s_null_leaves_it_and_their_false_reopens_it()
    {
        var s = await ScenarioAsync();

        (await s.Supervisor.PutAsJsonAsync($"/api/classifications/{s.CallId}", s.Save(resolved: true))).EnsureSuccessStatusCode();
        (await s.Supervisor.PutAsJsonAsync($"/api/classifications/{s.CallId}", s.Save(resolved: null, notes: "noted"))).EnsureSuccessStatusCode();
        (await RowAsync(s.CallId)).Resolved.Should().BeTrue();

        (await s.Supervisor.PutAsJsonAsync($"/api/classifications/{s.CallId}", s.Save(resolved: false))).EnsureSuccessStatusCode();
        var row = await RowAsync(s.CallId);
        row.Resolved.Should().BeFalse();
        row.ResolvedAt.Should().BeNull();
        row.ResolvedBy.Should().BeNull();
    }

    private Task<Classification> RowAsync(Guid callId) =>
        data.QueryAsync(db => db.Classifications.AsNoTracking().FirstAsync(c => c.CommunicationId == callId));

    private sealed record Scenario(HttpClient Agent, HttpClient Supervisor, Guid SupervisorId, Guid CallId, Guid ComplaintType, int FormVersion)
    {
        public object Save(bool? resolved, string notes = "complaint") => new
        {
            typeId = ComplaintType,
            branchId = (Guid?)null,
            orderValue = (decimal?)null,
            notes,
            followUp = true,
            resolved,
            formVersion = FormVersion,
            customValues = new { },
        };
    }

    /// <summary>An agent's answered call from a minute ago, still inside their edit window (A-42).</summary>
    private async Task<Scenario> ScenarioAsync()
    {
        await data.EnsurePhoneChannelAsync();
        var agentUser = await data.CreateUserAsync();
        var supervisorUser = await data.CreateUserAsync(UserRoles.Supervisor);
        var (agent, _) = await data.SignInAsync(agentUser);
        var (supervisor, _) = await data.SignInAsync(supervisorUser);

        var (callId, complaint, version) = await data.QueryAsync(async db =>
        {
            var channel = await db.Channels.Where(c => c.Name == ChannelNames.Phone).Select(c => c.Id).FirstAsync();

            // CI's database is migrated, not seeded: the system type may not be there.
            var complaintId = await db.ClassificationTypes.Where(t => t.Name == "Complaint").Select(t => (Guid?)t.Id).FirstOrDefaultAsync();
            if (complaintId is null)
            {
                var type = new ClassificationType { Name = "Complaint", LabelAr = "شكوى", LabelEn = "Complaint", IsSystem = true };
                db.ClassificationTypes.Add(type);
                await db.SaveChangesAsync();
                complaintId = type.Id;
            }

            var form = new FormDefinition
            {
                Version = 100_000 + Random.Shared.Next(0, 1_000_000_000),
                Definition = JsonDocument.Parse("""{"fields":[]}"""),
            };
            var call = new Communication
            {
                Kind = CommunicationKinds.Call,
                ChannelId = channel,
                Direction = Directions.In,
                Status = CommunicationStatuses.Answered,
                AgentId = agentUser.Id,
                RemoteNumberRaw = TestData.NewMobile(),
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                SipCallId = TestData.NewSipCallId(),
                Extension = agentUser.Extension,
                Source = CommunicationSources.AgentApp,
            };
            db.AddRange(form, call);
            await db.SaveChangesAsync();
            return (call.Id, complaintId.Value, form.Version);
        });

        var scenario = new Scenario(agent, supervisor, supervisorUser.Id, callId, complaint, version);

        // The agent classifies it first, as they would at hang-up.
        (await agent.PutAsJsonAsync($"/api/classifications/{callId}", scenario.Save(resolved: null)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        return scenario;
    }
}
