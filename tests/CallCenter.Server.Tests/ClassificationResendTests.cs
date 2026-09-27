using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The same classification sent more than once (M-S10, 27 Sep review): at the
/// same moment it is one classification, not a 500; sent again later it adds
/// no history row. Against a real database.
/// </summary>
[Collection(ApiCollection.Name)]
public class ClassificationResendTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task A_resend_that_changes_nothing_adds_no_history_row()
    {
        var (agent, callId, save) = await ScenarioAsync();

        (await agent.PutAsJsonAsync($"/api/classifications/{callId}", save("two burgers"))).EnsureSuccessStatusCode();
        (await agent.PutAsJsonAsync($"/api/classifications/{callId}", save("two burgers"))).EnsureSuccessStatusCode();
        (await agent.PutAsJsonAsync($"/api/classifications/{callId}", save("two burgers"))).EnsureSuccessStatusCode();

        (await HistoryAsync(callId)).Should().Be(1, "the first save; the two resends changed nothing");

        (await agent.PutAsJsonAsync($"/api/classifications/{callId}", save("three burgers"))).EnsureSuccessStatusCode();
        (await HistoryAsync(callId)).Should().Be(2, "a real change is still recorded");
    }

    [DatabaseFact]
    public async Task First_saves_racing_each_other_all_succeed_and_make_one_classification()
    {
        // Several rounds: the race needs two inserts in the same instant.
        for (var round = 0; round < 3; round++)
        {
            var (agent, callId, save) = await ScenarioAsync();

            var responses = await Task.WhenAll(Enumerable.Range(0, 4)
                .Select(_ => agent.PutAsJsonAsync($"/api/classifications/{callId}", save("same order"))));

            foreach (var response in responses)
            {
                response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
            }

            (await data.QueryAsync(db => db.Classifications.CountAsync(c => c.CommunicationId == callId))).Should().Be(1);
            (await HistoryAsync(callId)).Should().Be(1);
        }
    }

    private Task<int> HistoryAsync(Guid callId) =>
        data.QueryAsync(db => db.ClassificationHistory.CountAsync(h => h.CommunicationId == callId));

    /// <summary>An agent's answered call from a minute ago, not yet classified.</summary>
    private async Task<(HttpClient Agent, Guid CallId, Func<string, object> Save)> ScenarioAsync()
    {
        await data.EnsurePhoneChannelAsync();
        var owner = await data.CreateUserAsync();
        var (agent, _) = await data.SignInAsync(owner);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var (callId, typeId, version) = await data.QueryAsync(async db =>
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
                StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
                SipCallId = TestData.NewSipCallId(),
                Extension = owner.Extension,
                Source = CommunicationSources.AgentApp,
            };
            db.AddRange(type, form, call);
            await db.SaveChangesAsync();
            return (call.Id, type.Id, form.Version);
        });

        object Save(string notes) => new
        {
            typeId = typeId,
            branchId = (Guid?)null,
            orderValue = 20m,
            notes,
            followUp = false,
            formVersion = version,
            customValues = new { },
        };

        return (agent, callId, Save);
    }
}
