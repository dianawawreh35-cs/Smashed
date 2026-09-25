using System.Text.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Communications;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The nightly job that expires recordings (A-33), and the storage usage it
/// reports (S-43).
/// </summary>
/// <remarks>
/// <b>The requirement is a pair of opposites</b>, so the tests are too: the
/// audio must go, and everything else must stay. A job that deleted the row as
/// well would pass any test that only looked at the file, and would quietly
/// rewrite last year's reports — N-08 keeps communications and classifications
/// indefinitely, and a call whose audio expired must read as <i>recorded,
/// expired</i> rather than as one that was never recorded.
///
/// Against a real database, because every one of those claims is a row.
/// </remarks>
[Collection(ApiCollection.Name)]
public class RecordingRetentionTests
{
    private readonly CallCenterApiFactory _factory;
    private readonly TestData _data;
    private readonly RecordingFixtures _recordings;

    public RecordingRetentionTests(CallCenterApiFactory factory)
    {
        _factory = factory;
        _data = new TestData(factory);
        _recordings = new RecordingFixtures(factory, _data);
    }


    [DatabaseFact]
    public async Task An_expired_recording_loses_its_file_and_keeps_its_row()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        var recording = await _recordings.AttachRecordingAsync(call, uploadedDaysAgo: 120);

        _recordings.Stored(recording.Path).Should().BeTrue("the test wrote the file it is about to expire");

        var result = await _recordings.RunRetentionAsync();

        result.Ran.Should().BeTrue();
        result.Expired.Should().BeGreaterThanOrEqualTo(1);

        _recordings.Stored(recording.Path).Should().BeFalse("the audio is what expires");

        var after = await _recordings.RecordingForAsync(call.Id);
        after.Should().NotBeNull("the row is kept — only the file goes");
        after!.DeletedAt.Should().NotBeNull("a deleted recording must read as expired, not as never recorded");
        after.Path.Should().Be(recording.Path, "the path is history, not a promise that the file is there");
    }

    /// <summary>
    /// The half of A-33 that is easy to break and hard to notice: the call and
    /// its classification are kept indefinitely (N-08), so a report over last
    /// year stays correct after the audio has gone.
    /// </summary>
    [DatabaseFact]
    public async Task The_call_and_its_classification_survive_the_expiry()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        await _recordings.AttachRecordingAsync(call, uploadedDaysAgo: 120);
        await ClassifyAsync(call, agent, orderValue: 42.5m);

        await _recordings.RunRetentionAsync();

        var kept = await _data.QueryAsync(db => db.Communications
            .AsNoTracking()
            .Include(c => c.Classification)
            .FirstOrDefaultAsync(c => c.Id == call.Id));

        kept.Should().NotBeNull("the call record is kept indefinitely (N-08)");
        kept!.Status.Should().Be(CommunicationStatuses.Answered);
        kept.AgentId.Should().Be(agent.Id);
        kept.Classification.Should().NotBeNull("the classification is kept too");
        kept.Classification!.OrderValue.Should().Be(42.5m);
    }

    [DatabaseFact]
    public async Task A_recording_inside_the_retention_period_is_left_alone()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        var recording = await _recordings.AttachRecordingAsync(call, uploadedDaysAgo: 3);

        await _recordings.RunRetentionAsync();

        _recordings.Stored(recording.Path).Should().BeTrue("three days is well inside ninety");
        (await _recordings.RecordingForAsync(call.Id))!.DeletedAt.Should().BeNull();
    }

    /// <summary>
    /// A supervisor changing 90 days to 30 must not need the server restarted
    /// (A-33). The setting is read at the start of every run.
    /// </summary>
    [DatabaseFact]
    public async Task The_retention_period_is_read_on_every_run()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        var recording = await _recordings.AttachRecordingAsync(call, uploadedDaysAgo: 10);

        await _recordings.RunRetentionAsync();
        _recordings.Stored(recording.Path).Should().BeTrue("ten days is inside the default ninety");

        // Put back what it was, not the default: the setting is the database's,
        // and a run should leave it as it found it.
        var previous = await _data.QueryAsync(db => db.Settings
            .Where(s => s.Key == RecordingRetention.RetentionDaysKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync());

        await _recordings.SetRetentionDaysAsync(5);

        try
        {
            // Same process and same registrations as the run before it: nothing
            // has been restarted between the two.
            await _recordings.RunRetentionAsync();

            _recordings.Stored(recording.Path).Should().BeFalse("the shorter period applies to the very next run");
            (await _recordings.RecordingForAsync(call.Id))!.DeletedAt.Should().NotBeNull();
        }
        finally
        {
            await _recordings.SetRetentionDaysAsync(
                int.TryParse(previous, out var days) ? days : RecordingRetention.DefaultRetentionDays);
        }
    }

    /// <summary>
    /// A-33's third rule: one file that is not where the row says it is must be
    /// a warning and a job that carries on, not a job that stops.
    /// </summary>
    [DatabaseFact]
    public async Task A_recording_whose_file_has_already_gone_does_not_stop_the_run()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);

        var orphaned = await _recordings.CreateCallAsync(agent);
        var orphanedRecording = await _recordings.AttachRecordingAsync(orphaned, uploadedDaysAgo: 200);
        _recordings.DeleteFromDisk(orphanedRecording.Path);

        var good = await _recordings.CreateCallAsync(agent);
        var goodRecording = await _recordings.AttachRecordingAsync(good, uploadedDaysAgo: 150);

        var result = await _recordings.RunRetentionAsync();

        result.Ran.Should().BeTrue();
        result.AlreadyGone.Should().BeGreaterThanOrEqualTo(1);

        _recordings.Stored(goodRecording.Path).Should().BeFalse("the run carried on past the missing file");
        (await _recordings.RecordingForAsync(good.Id))!.DeletedAt.Should().NotBeNull();

        (await _recordings.RecordingForAsync(orphaned.Id))!.DeletedAt.Should()
            .NotBeNull("a row whose audio is gone still has to say so, or the call reads as never recorded");
    }

    /// <summary>
    /// The storage usage view (S-43) — the number that says whether ninety days
    /// is affordable.
    /// </summary>
    [DatabaseFact]
    public async Task Storage_usage_counts_what_is_kept_and_what_has_expired()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);

        var kept = await _recordings.CreateCallAsync(agent);
        var keptRecording = await _recordings.AttachRecordingAsync(kept, uploadedDaysAgo: 1);

        var old = await _recordings.CreateCallAsync(agent);
        await _recordings.AttachRecordingAsync(old, uploadedDaysAgo: 400);

        var before = await _recordings.UsageAsync();
        before.RetentionDays.Should().Be(RecordingRetention.DefaultRetentionDays);
        before.Kept.Should().BeGreaterThanOrEqualTo(2);
        before.KeptBytes.Should().BeGreaterThanOrEqualTo(keptRecording.SizeBytes!.Value);
        before.DiskReadable.Should().BeTrue("the recordings folder exists once anything has been written");
        before.DiskFiles.Should().BeGreaterThanOrEqualTo(2);
        before.NewestKept.Should().NotBeNull();

        await _recordings.RunRetentionAsync();

        var after = await _recordings.UsageAsync();
        after.Expired.Should().BeGreaterThan(before.Expired, "the four-hundred-day-old recording expired");
        after.Kept.Should().BeLessThan(before.Kept);
    }

    // ---- helpers ------------------------------------------------------------

    private async Task ClassifyAsync(Communication call, User agent, decimal orderValue)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var type = await db.ClassificationTypes.FirstOrDefaultAsync();

        if (type is null)
        {
            type = new ClassificationType
            {
                Name = $"Test-{Guid.NewGuid():N}"[..12],
                LabelAr = "اختبار",
                LabelEn = "Test",
                IsSystem = false,
            };

            db.ClassificationTypes.Add(type);
            await db.SaveChangesAsync();
        }

        var form = await db.FormDefinitions.OrderByDescending(f => f.Version).FirstOrDefaultAsync();

        if (form is null)
        {
            form = new FormDefinition
            {
                Version = 1,
                Definition = JsonDocument.Parse("""{"fields":[]}"""),
                IsCurrent = false,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            db.FormDefinitions.Add(form);
            await db.SaveChangesAsync();
        }

        db.Classifications.Add(new Classification
        {
            CommunicationId = call.Id,
            TypeId = type.Id,
            OrderValue = orderValue,
            FormVersion = form.Version,
            CustomValues = JsonDocument.Parse("{}"),
            ClassifiedBy = agent.Id,
            ClassifiedAt = DateTimeOffset.UtcNow,
        });

        await db.SaveChangesAsync();
    }

}
