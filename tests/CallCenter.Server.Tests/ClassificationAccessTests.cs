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
/// Who may read a call's classification and its history (A-52, A-43): a
/// supervisor any call, an agent only their own. Against a real database.
/// </summary>
/// <remarks>
/// Both endpoints once answered any signed-in account for any call, so one
/// agent could read what another's customer ordered or complained about, and
/// who changed it.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ClassificationAccessTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task Another_agent_is_refused_the_classification_and_its_history()
    {
        var (_, callId) = await ClassifiedCallAsync();
        var (other, _) = await data.SignInAsync(await data.CreateUserAsync());

        await ShouldBeNotYoursAsync(await other.GetAsync($"/api/classifications/{callId}"));
        await ShouldBeNotYoursAsync(await other.GetAsync($"/api/classifications/{callId}/history"));
    }

    [DatabaseFact]
    public async Task The_agent_whose_call_it_is_reads_both()
    {
        // What the Agent App does when the agent opens their own call from the log.
        var (owner, callId) = await ClassifiedCallAsync();
        var (client, _) = await data.SignInAsync(owner);

        var classification = await client.GetFromJsonAsync<ClassificationDto>($"/api/classifications/{callId}");
        var history = await client.GetFromJsonAsync<List<ClassificationHistoryDto>>($"/api/classifications/{callId}/history");

        classification!.Notes.Should().Be("wants extra sauce");
        history.Should().ContainSingle();
    }

    [DatabaseFact]
    public async Task A_supervisor_reads_both_for_any_agent_s_call()
    {
        var (_, callId) = await ClassifiedCallAsync();
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));

        (await supervisor.GetAsync($"/api/classifications/{callId}")).StatusCode.Should().Be(HttpStatusCode.OK);
        var history = await supervisor.GetFromJsonAsync<List<ClassificationHistoryDto>>($"/api/classifications/{callId}/history");
        history.Should().ContainSingle();
    }

    [DatabaseFact]
    public async Task A_call_that_does_not_exist_is_still_a_404_and_an_empty_history()
    {
        // Unchanged, and telling an agent nothing about a call that is not there.
        var (agent, _) = await data.SignInAsync(await data.CreateUserAsync());
        var nobody = Guid.NewGuid();

        (await agent.GetAsync($"/api/classifications/{nobody}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await agent.GetFromJsonAsync<List<ClassificationHistoryDto>>($"/api/classifications/{nobody}/history"))
            .Should().BeEmpty();
    }

    [DatabaseFact]
    public async Task A_supervisor_changes_any_agent_s_classification_days_later_and_it_is_recorded()
    {
        // S-04, from the call screen: another agent's call, from days ago, when
        // the agent's own edit window has long closed (A-42).
        var (_, callId) = await ClassifiedCallAsync(DateTimeOffset.UtcNow.AddDays(-3));
        var supervisorAccount = await data.CreateUserAsync(UserRoles.Supervisor);
        var (supervisor, _) = await data.SignInAsync(supervisorAccount);
        var before = await supervisor.GetFromJsonAsync<ClassificationDto>($"/api/classifications/{callId}");

        var response = await supervisor.PutAsJsonAsync($"/api/classifications/{callId}", new
        {
            typeId = before!.TypeId,
            branchId = (Guid?)null,
            orderValue = 52m,
            notes = "corrected by the supervisor",
            followUp = true,
            formVersion = before.FormVersion,
            customValues = new { },
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var after = await supervisor.GetFromJsonAsync<ClassificationDto>($"/api/classifications/{callId}");
        after!.Notes.Should().Be("corrected by the supervisor");
        after.OrderValue.Should().Be(52m);
        after.UpdatedByName.Should().Be(supervisorAccount.DisplayName);

        var history = await supervisor.GetFromJsonAsync<List<ClassificationHistoryDto>>($"/api/classifications/{callId}/history");
        history!.Should().HaveCount(2, "the first classification and the supervisor's change");
        history[0].ChangedByName.Should().Be(supervisorAccount.DisplayName, "newest first");
    }

    private static async Task ShouldBeNotYoursAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("not_your_call");
    }

    /// <summary>An agent's answered call, classified, with one history row.</summary>
    private async Task<(User Owner, Guid CallId)> ClassifiedCallAsync(DateTimeOffset? startedAt = null)
    {
        await data.EnsurePhoneChannelAsync();
        var owner = await data.CreateUserAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var callId = await data.QueryAsync(async db =>
        {
            var channel = await db.Channels.Where(c => c.Name == ChannelNames.Phone).Select(c => c.Id).FirstAsync();
            var type = new ClassificationType { Name = $"AccessType{suffix}", LabelAr = "طلب", LabelEn = "Order" };
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
                AgentId = owner.Id,
                RemoteNumberRaw = TestData.NewMobile(),
                StartedAt = startedAt ?? DateTimeOffset.UtcNow.AddMinutes(-10),
                SipCallId = TestData.NewSipCallId(),
                Extension = owner.Extension,
                Source = CommunicationSources.AgentApp,
            };
            db.AddRange(type, form, call);
            await db.SaveChangesAsync();

            db.Classifications.Add(new Classification
            {
                CommunicationId = call.Id,
                TypeId = type.Id,
                Notes = "wants extra sauce",
                FormVersion = form.Version,
                CustomValues = JsonDocument.Parse("{}"),
                ClassifiedBy = owner.Id,
                ClassifiedAt = DateTimeOffset.UtcNow,
            });
            db.ClassificationHistory.Add(new ClassificationHistory
            {
                CommunicationId = call.Id,
                ChangedBy = owner.Id,
                ChangedAt = DateTimeOffset.UtcNow,
                After = JsonDocument.Parse("""{"notes":"wants extra sauce"}"""),
            });
            await db.SaveChangesAsync();
            return call.Id;
        });

        return (owner, callId);
    }
}
