using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Server.Features.Communications;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Uploading the audio of a finished call and attaching it to that call
/// (A-31). Against a real database, because the whole point is the row in
/// <c>recordings</c> and the file beside it.
/// </summary>
/// <remarks>
/// The case that matters most is the one that looks like a failure: a recording
/// arriving for a call the server has not been told about yet. The Agent App
/// reports the call and uploads the audio as two items in one queue, and if the
/// order ever slips the answer must be "not yet" — a refusal the app retries —
/// rather than a loss or, far worse, an attachment to somebody else's call.
/// </remarks>
[Collection(ApiCollection.Name)]
public class RecordingUploadTests(CallCenterApiFactory factory)
{
    private const string Password = "correct horse battery";

    [DatabaseFact]
    public async Task A_recording_is_stored_and_attached_to_its_call()
    {
        var agent = await CreateAgentAsync();
        var call = await CreateCallAsync(agent);
        var client = await SignInAsync(agent);

        var response = await client.PostAsync(
            "/api/communications/recordings", Upload(call.SipCallId!, agent.Extension!));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var recording = await RecordingFor(call.Id);
        recording.Should().NotBeNull("the call now has audio attached to it");
        recording!.SizeBytes.Should().BeGreaterThan(0);
        recording.Format.Should().Be("wav");
        recording.DeletedAt.Should().BeNull();

        // Laid out by date, as SCHEMA.md specifies, and named for the call.
        recording.Path.Should().Be(RecordingStore.PathFor(call.Id, call.StartedAt));

        Stored(recording.Path).Should().BeTrue("the bytes belong on disk, not in the database");
    }

    [DatabaseFact]
    public async Task A_recording_for_a_call_the_server_has_not_heard_of_is_deferred_not_lost()
    {
        var agent = await CreateAgentAsync();
        var client = await SignInAsync(agent);

        var response = await client.PostAsync(
            "/api/communications/recordings",
            Upload($"never-reported-{Guid.NewGuid():N}@pbx", agent.Extension!));

        // 404 and "call_not_found" together are what the Agent App's queue
        // defers on. Any other refusal would make it discard the audio.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCode(response)).Should().Be("call_not_found");
    }

    [DatabaseFact]
    public async Task An_agent_cannot_attach_a_recording_to_another_agents_call()
    {
        var owner = await CreateAgentAsync();
        var intruder = await CreateAgentAsync();
        var call = await CreateCallAsync(owner);

        var client = await SignInAsync(intruder);

        var response = await client.PostAsync(
            "/api/communications/recordings", Upload(call.SipCallId!, owner.Extension!));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await RecordingFor(call.Id)).Should().BeNull("nothing was attached");
    }

    /// <summary>
    /// The offline queue resends, so the same call may be uploaded twice. The
    /// second must replace the first: a second row would break the unique
    /// constraint, and a second file would leave one silently orphaned.
    /// </summary>
    [DatabaseFact]
    public async Task Uploading_the_same_call_twice_replaces_rather_than_duplicates()
    {
        var agent = await CreateAgentAsync();
        var call = await CreateCallAsync(agent);
        var client = await SignInAsync(agent);

        await client.PostAsync("/api/communications/recordings", Upload(call.SipCallId!, agent.Extension!));
        var first = await RecordingFor(call.Id);

        var second = await client.PostAsync(
            "/api/communications/recordings", Upload(call.SipCallId!, agent.Extension!, seconds: 2));

        second.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        (await db.Recordings.CountAsync(r => r.CommunicationId == call.Id))
            .Should().Be(1, "one call has one recording");

        var after = await RecordingFor(call.Id);
        after!.SizeBytes.Should().BeGreaterThan(first!.SizeBytes!.Value, "the longer audio replaced the shorter");
    }

    [DatabaseFact]
    public async Task A_request_with_no_audio_is_refused_before_anything_is_written()
    {
        var agent = await CreateAgentAsync();
        var call = await CreateCallAsync(agent);
        var client = await SignInAsync(agent);

        var form = new MultipartFormDataContent
        {
            { new StringContent(call.SipCallId!), "sipCallId" },
            { new StringContent(agent.Extension!), "extension" },
        };

        var response = await client.PostAsync("/api/communications/recordings", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await RecordingFor(call.Id)).Should().BeNull();
    }

    // ---- helpers ------------------------------------------------------------

    /// <summary>
    /// A plausible recording: a stereo mu-law WAV of the size the Agent App
    /// produces, so the length actually distinguishes one upload from another.
    /// </summary>
    private static MultipartFormDataContent Upload(string sipCallId, string extension, int seconds = 1)
    {
        var audio = new byte[58 + (8000 * 2 * seconds)];
        Array.Fill(audio, (byte)0xFF, 58, audio.Length - 58);

        var content = new MultipartFormDataContent
        {
            { new StringContent(sipCallId), "sipCallId" },
            { new StringContent(extension), "extension" },
        };

        var file = new ByteArrayContent(audio);
        file.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(file, "audio", "call.wav");

        return content;
    }

    private async Task<HttpClient> SignInAsync(User agent)
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(agent.Login, Password, LaptopId: "LAPTOP-TEST", AppVersion: "test"));

        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);

        return client;
    }

    private async Task<User> CreateAgentAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISipSecretProtector>();

        var suffix = Guid.NewGuid().ToString("N")[..8];

        var agent = new User
        {
            Login = $"rec-agent-{suffix}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            DisplayName = $"Recording agent {suffix}",
            Role = UserRoles.Agent,
            Extension = $"8{Random.Shared.Next(100, 999)}",
            SipSecret = secrets.Protect("sip-secret"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Users.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    private async Task<Communication> CreateCallAsync(User agent)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        // Created rather than assumed: migrations build the tables, seeding
        // fills them, and a test that needs a row should not depend on whether
        // somebody seeded the database it happens to be pointed at.
        var channel = await db.Channels.FirstOrDefaultAsync(c => c.Name == ChannelNames.Phone);

        if (channel is null)
        {
            channel = new Channel { Name = ChannelNames.Phone, IsSystem = true, SortOrder = 0 };
            db.Channels.Add(channel);
            await db.SaveChangesAsync();
        }

        var call = new Communication
        {
            Kind = CommunicationKinds.Call,
            ChannelId = channel.Id,
            Direction = Directions.In,
            Status = CommunicationStatuses.Answered,
            AgentId = agent.Id,
            Extension = agent.Extension,
            SipCallId = $"rec-{Guid.NewGuid():N}@pbx",
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Source = CommunicationSources.AgentApp,
        };

        db.Communications.Add(call);
        await db.SaveChangesAsync();
        return call;
    }

    private async Task<Recording?> RecordingFor(Guid communicationId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        return await db.Recordings.AsNoTracking()
            .FirstOrDefaultAsync(r => r.CommunicationId == communicationId);
    }

    private bool Stored(string relativePath)
    {
        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<RecordingStore>();

        using var stream = store.Open(relativePath);
        return stream is not null;
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem?.TryGetValue("code", out var code) == true ? code?.ToString() : null;
    }
}
