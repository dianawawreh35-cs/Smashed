using System.Net.Http.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Communications;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The recording mark in the agent's call log (A-50): which calls have audio,
/// and which had it until retention removed it (A-33).
/// </summary>
/// <remarks>
/// Against a real database, because the answer is the <c>recordings</c> row
/// and its <c>deleted_at</c>. The case worth a test is the expired one: it
/// must not come back looking like a call that was never recorded, or an agent
/// looking at last month's calls sees what reads as a recorder that failed.
/// </remarks>
[Collection(ApiCollection.Name)]
public class CallLogRecordingTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task The_call_log_tells_kept_expired_and_never_recorded_apart()
    {
        var agent = await data.CreateUserAsync();

        var kept = await CreateCallAsync(agent);
        var expired = await CreateCallAsync(agent);
        var never = await CreateCallAsync(agent);

        await AttachRecordingAsync(kept, deletedAt: null);
        await AttachRecordingAsync(expired, deletedAt: DateTimeOffset.UtcNow.AddDays(-1));

        var (client, _) = await data.SignInAsync(agent);
        var calls = await client.GetFromJsonAsync<List<CommunicationDto>>("/api/communications/mine?limit=10");

        calls.Should().NotBeNull();

        var byId = calls!.ToDictionary(c => c.Id);

        byId[kept.Id].HasRecording.Should().BeTrue();
        byId[kept.Id].RecordingExpired.Should().BeFalse();

        byId[expired.Id].HasRecording.Should().BeFalse("retention removed the audio");
        byId[expired.Id].RecordingExpired.Should().BeTrue("the call was recorded, and that is not forgotten");

        byId[never.Id].HasRecording.Should().BeFalse();
        byId[never.Id].RecordingExpired.Should().BeFalse("nothing was ever recorded, so nothing expired");
    }

    private async Task<Communication> CreateCallAsync(User agent)
    {
        await data.EnsurePhoneChannelAsync();

        var channelId = await data.QueryAsync(db =>
            db.Channels.Where(c => c.Name == ChannelNames.Phone).Select(c => c.Id).FirstAsync());

        var call = new Communication
        {
            Kind = CommunicationKinds.Call,
            ChannelId = channelId,
            Direction = Directions.In,
            Status = CommunicationStatuses.Answered,
            AgentId = agent.Id,
            Extension = agent.Extension,
            SipCallId = TestData.NewSipCallId(),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            AnsweredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            EndedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            DurationSec = 60,
            Source = CommunicationSources.AgentApp,
        };

        return await data.QueryAsync(async db =>
        {
            db.Communications.Add(call);
            await db.SaveChangesAsync();
            return call;
        });
    }

    /// <summary>
    /// The row alone. The list reads the row, not the file, so no audio needs
    /// to be written for this.
    /// </summary>
    private Task AttachRecordingAsync(Communication call, DateTimeOffset? deletedAt) =>
        data.QueryAsync(async db =>
        {
            db.Recordings.Add(new Recording
            {
                CommunicationId = call.Id,
                Path = RecordingStore.PathFor(call.Id, call.StartedAt),
                SizeBytes = 16_058,
                DurationSec = 1,
                UploadedAt = call.EndedAt!.Value,
                DeletedAt = deletedAt,
            });

            return await db.SaveChangesAsync();
        });
}
