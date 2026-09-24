using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Communications;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CallCenter.Server.Tests;

/// <summary>
/// Calls with real audio behind them, for the retention (A-33) and playback
/// (A-51, S-04) tests.
/// </summary>
/// <remarks>
/// Beside <see cref="TestData"/> rather than inside it: everything here writes
/// files as well as rows, and a test that only needs a user should not drag the
/// recordings folder in with it.
///
/// The audio is written through <see cref="RecordingStore"/> itself, never by
/// placing a file by hand, so the layout under test is the layout production
/// uses — the retention job walks it and the playback endpoint opens it.
/// </remarks>
public class RecordingFixtures(CallCenterApiFactory factory, TestData data)
{
    /// <summary>A call belonging to <paramref name="agent"/>, answered five minutes ago.</summary>
    public async Task<Communication> CreateCallAsync(User agent)
    {
        await data.EnsurePhoneChannelAsync();

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var channel = await db.Channels.FirstAsync(c => c.Name == ChannelNames.Phone);

        var call = new Communication
        {
            Kind = CommunicationKinds.Call,
            ChannelId = channel.Id,
            Direction = Directions.In,
            Status = CommunicationStatuses.Answered,
            AgentId = agent.Id,
            Extension = agent.Extension,
            SipCallId = TestData.NewSipCallId(),
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Source = CommunicationSources.AgentApp,
        };

        db.Communications.Add(call);
        await db.SaveChangesAsync();
        return call;
    }

    /// <summary>
    /// A real file on disk and a row pointing at it, dated as if it had been
    /// uploaded <paramref name="uploadedDaysAgo"/> days ago.
    /// </summary>
    public async Task<Recording> AttachRecordingAsync(
        Communication call, int uploadedDaysAgo = 0, int seconds = 1)
    {
        var uploadedAt = DateTimeOffset.UtcNow.AddDays(-uploadedDaysAgo);

        await using var scope = factory.Services.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<RecordingStore>();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        await using var audio = new MemoryStream(Audio(seconds));
        var (path, size) = await store.SaveAsync(call.Id, uploadedAt, audio);

        var recording = new Recording
        {
            CommunicationId = call.Id,
            Path = path,
            SizeBytes = size,
            Format = "wav",
            UploadedAt = uploadedAt,
        };

        db.Recordings.Add(recording);
        await db.SaveChangesAsync();
        return recording;
    }

    /// <summary>One pass of the retention job (A-33), as the nightly worker runs it.</summary>
    public async Task<RecordingRetention.Result> RunRetentionAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RecordingRetention>().RunOnceAsync();
    }

    /// <summary>The storage usage the endpoint reports (S-43).</summary>
    public async Task<RecordingStorageDto> UsageAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<RecordingRetention>().UsageAsync();
    }

    /// <summary>Sets <c>recording.retention_days</c> as the settings screen would (S-43, S-47).</summary>
    public async Task SetRetentionDaysAsync(int days)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var row = await db.Settings.FirstOrDefaultAsync(s => s.Key == RecordingRetention.RetentionDaysKey);

        if (row is null)
        {
            db.Settings.Add(new Setting
            {
                Key = RecordingRetention.RetentionDaysKey,
                Value = days.ToString(),
                UpdatedAt = DateTimeOffset.UtcNow,
            });
        }
        else
        {
            row.Value = days.ToString();
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await db.SaveChangesAsync();
    }

    public async Task<Recording?> RecordingForAsync(Guid communicationId) =>
        await data.QueryAsync(db => db.Recordings.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CommunicationId == communicationId));

    /// <summary>Whether the audio is still on disk.</summary>
    public bool Stored(string relativePath)
    {
        using var scope = factory.Services.CreateScope();
        using var stream = scope.ServiceProvider.GetRequiredService<RecordingStore>().Open(relativePath);
        return stream is not null;
    }

    /// <summary>Removes a file behind its row's back, as a tidy-up or a lost mount would.</summary>
    public void DeleteFromDisk(string relativePath)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<RecordingStore>().DeleteFile(relativePath);
    }

    /// <summary>
    /// Audio of the shape the Agent App produces: a stereo mu-law WAV, one
    /// second per <paramref name="seconds"/>, filled with G.711 silence (0xFF,
    /// not zero — see the 24 September recording entry).
    /// </summary>
    public static byte[] Audio(int seconds = 1)
    {
        var audio = new byte[58 + (8000 * 2 * seconds)];
        Array.Fill(audio, (byte)0xFF, 58, audio.Length - 58);
        return audio;
    }
}
