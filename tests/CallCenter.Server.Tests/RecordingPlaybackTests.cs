using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Communications;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Hearing a recording back (A-51, S-04) and the storage usage view (S-43).
/// </summary>
/// <remarks>
/// <b>Who may hear what is the whole test class.</b> A supervisor plays and
/// downloads any agent's recording (S-04); an agent plays their own (A-51) and
/// is refused anybody else's (A-52). The refusal is the one that matters and
/// the one easiest to leave out, so it is written first among the three and
/// checks the file was never opened rather than only the status code.
///
/// The endpoints are reached over HTTP with a real token, because the rule
/// being tested is an authorization rule and calling the service directly would
/// skip exactly the part that enforces it.
/// </remarks>
[Collection(ApiCollection.Name)]
public class RecordingPlaybackTests
{
    private readonly TestData _data;
    private readonly RecordingFixtures _recordings;
    private readonly CallCenterApiFactory _factory;

    public RecordingPlaybackTests(CallCenterApiFactory factory)
    {
        _factory = factory;
        _data = new TestData(factory);
        _recordings = new RecordingFixtures(factory, _data);
    }

    /// <summary>
    /// A-52. The one that must not be missed: an agent must not be able to
    /// fetch another agent's audio.
    /// </summary>
    [DatabaseFact]
    public async Task An_agent_is_refused_another_agents_recording()
    {
        var owner = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(owner);
        var recording = await _recordings.AttachRecordingAsync(call);

        var intruder = await _data.CreateUserAsync(UserRoles.Agent);
        var (client, _) = await _data.SignInAsync(intruder);

        var response = await client.GetAsync($"/api/recordings/{call.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ErrorCode(response)).Should().Be("not_your_call");

        _recordings.Stored(recording.Path).Should().BeTrue("a refusal must not touch the file");
    }

    /// <summary>A-51: the player on the agent's own call details.</summary>
    [DatabaseFact]
    public async Task An_agent_plays_their_own_recording()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        var recording = await _recordings.AttachRecordingAsync(call);

        var (client, _) = await _data.SignInAsync(agent);

        var response = await client.GetAsync($"/api/recordings/{call.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("audio/wav");

        var audio = await response.Content.ReadAsByteArrayAsync();
        audio.Length.Should().Be((int)recording.SizeBytes!.Value, "the whole recording is served");
    }

    /// <summary>S-04: a supervisor plays any agent's recording.</summary>
    [DatabaseFact]
    public async Task A_supervisor_plays_another_agents_recording()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        await _recordings.AttachRecordingAsync(call);

        var supervisor = await _data.CreateUserAsync(UserRoles.Supervisor);
        var (client, _) = await _data.SignInAsync(supervisor);

        var response = await client.GetAsync($"/api/recordings/{call.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Length.Should().BeGreaterThan(0);
    }

    /// <summary>
    /// S-04 again: the download, which offers the audio as a file to keep and
    /// names it after the call it came from.
    /// </summary>
    [DatabaseFact]
    public async Task A_supervisor_downloads_a_recording_as_a_named_file()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        await _recordings.AttachRecordingAsync(call);

        var supervisor = await _data.CreateUserAsync(UserRoles.Supervisor);
        var (client, _) = await _data.SignInAsync(supervisor);

        var response = await client.GetAsync($"/api/recordings/{call.Id}/download");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var disposition = response.Content.Headers.ContentDisposition;
        disposition.Should().NotBeNull("a download is offered as a file, not played in the page");
        disposition!.DispositionType.Should().Be("attachment");
        (disposition.FileNameStar ?? disposition.FileName).Should()
            .Contain(agent.Extension!, "the file names the call it came from");
    }

    /// <summary>
    /// The narrower reading of the requirements: S-04 gives the supervisor
    /// "play and download", A-51 gives the agent "play the recording" and no
    /// more. See the decision entry of 24 September.
    /// </summary>
    [DatabaseFact]
    public async Task An_agent_cannot_download_even_their_own_recording()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        await _recordings.AttachRecordingAsync(call);

        var (client, _) = await _data.SignInAsync(agent);

        var response = await client.GetAsync($"/api/recordings/{call.Id}/download");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// A player seeks by asking for a range. The framework answers it because
    /// the file on disk is seekable — see the decision entry.
    /// </summary>
    [DatabaseFact]
    public async Task A_player_can_seek_because_ranges_are_answered()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        await _recordings.AttachRecordingAsync(call, seconds: 2);

        var (client, _) = await _data.SignInAsync(agent);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/recordings/{call.Id}");
        request.Headers.Range = new RangeHeaderValue(100, 199);

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent);
        (await response.Content.ReadAsByteArrayAsync()).Length.Should().Be(100);
    }

    /// <summary>
    /// A-33 meeting S-04: once retention has taken the audio, the answer has to
    /// let the screen say <i>expired</i> rather than <i>missing</i>.
    /// </summary>
    [DatabaseFact]
    public async Task An_expired_recording_answers_expired_rather_than_missing()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        await _recordings.AttachRecordingAsync(call, uploadedDaysAgo: 400);

        await _recordings.RunRetentionAsync();

        var supervisor = await _data.CreateUserAsync(UserRoles.Supervisor);
        var (client, _) = await _data.SignInAsync(supervisor);

        var response = await client.GetAsync($"/api/recordings/{call.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCode(response)).Should()
            .Be("recording_expired", "the call was recorded; the audio has aged out (A-33)");
    }

    [DatabaseFact]
    public async Task A_call_that_was_never_recorded_says_so()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);

        var supervisor = await _data.CreateUserAsync(UserRoles.Supervisor);
        var (client, _) = await _data.SignInAsync(supervisor);

        var response = await client.GetAsync($"/api/recordings/{call.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ErrorCode(response)).Should().Be("recording_not_found");
    }

    /// <summary>S-43, over HTTP: the supervisor's storage usage view.</summary>
    [DatabaseFact]
    public async Task A_supervisor_sees_how_much_room_the_recordings_take()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var call = await _recordings.CreateCallAsync(agent);
        var recording = await _recordings.AttachRecordingAsync(call);

        var supervisor = await _data.CreateUserAsync(UserRoles.Supervisor);
        var (client, _) = await _data.SignInAsync(supervisor);

        var usage = await client.GetFromJsonAsync<RecordingStorageDto>("/api/recordings/storage");

        usage.Should().NotBeNull();
        usage!.RetentionDays.Should().BeGreaterThan(0);
        usage.Kept.Should().BeGreaterThanOrEqualTo(1);
        usage.KeptBytes.Should().BeGreaterThanOrEqualTo(recording.SizeBytes!.Value);
        usage.DiskReadable.Should().BeTrue();
        usage.DiskFiles.Should().BeGreaterThanOrEqualTo(1);
    }

    /// <summary>
    /// Storage is a supervisor's number, like the settings screen it belongs
    /// beside. No database needed: the policy answers before any query.
    /// </summary>
    [DatabaseFact]
    public async Task An_agent_is_refused_the_storage_view()
    {
        var agent = await _data.CreateUserAsync(UserRoles.Agent);
        var (client, _) = await _data.SignInAsync(agent);

        var response = await client.GetAsync("/api/recordings/storage");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// N-05: recordings are served to authorised users and to nobody else. This
    /// one needs no database — the token is missing, so nothing is ever looked
    /// up.
    /// </summary>
    [Fact]
    public async Task A_request_with_no_token_is_refused()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync($"/api/recordings/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<string?> ErrorCode(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem?.TryGetValue("code", out var code) == true ? code?.ToString() : null;
    }
}
