using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Who may file a call, and what is refused before the database (A-14).
/// </summary>
/// <remarks>
/// The door is the interesting part here. A call is always attributed to the
/// account in the token, never to an id in the body, so one agent cannot file a
/// call against another — and there is no endpoint shape that would let them.
/// These run without PostgreSQL, so they stop at authorization and validation.
/// </remarks>
[Collection(ApiCollection.Name)]
public class CommunicationsEndpointTests(CallCenterApiFactory factory)
{
    private static object ValidCall(string status = CommunicationStatuses.Answered) => new
    {
        sipCallId = "abc123@pbx",
        extension = "2001",
        direction = Directions.In,
        status,
        remoteNumber = "0599123456",
        startedAt = DateTimeOffset.UtcNow,
        endedAt = DateTimeOffset.UtcNow,
    };

    [Fact]
    public async Task Without_a_token_a_call_cannot_be_filed()
    {
        var response = await factory.CreateClient()
            .PostAsJsonAsync("/api/communications/calls", ValidCall());

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_supervisor_cannot_file_a_call()
    {
        // A supervisor has no extension and takes no calls. They read the same
        // data through the reports; letting them post one would put a call in
        // the figures that never happened.
        var response = await ClientFor(UserRoles.Supervisor)
            .PostAsJsonAsync("/api/communications/calls", ValidCall());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_gets_past_the_door()
    {
        var response = await ClientFor(UserRoles.Agent)
            .PostAsJsonAsync("/api/communications/calls", ValidCall());

        // Past authorization it reaches the controller and then fails on the
        // database these tests deliberately do without.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(CommunicationStatuses.Answered)]
    [InlineData(CommunicationStatuses.Missed)]
    [InlineData(CommunicationStatuses.Rejected)]
    [InlineData(CommunicationStatuses.Blocked)]
    public async Task Every_outcome_the_app_reports_is_accepted(string status)
    {
        // A-14 wants all of these, and A-17 wants Blocked in particular. A
        // status the server quietly refuses would lose calls from the reports
        // with nothing to show for it.
        var response = await ClientFor(UserRoles.Agent)
            .PostAsJsonAsync("/api/communications/calls", ValidCall(status));

        response.StatusCode.Should().NotBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_status_this_system_does_not_know_is_refused()
    {
        var response = await ClientFor(UserRoles.Agent)
            .PostAsJsonAsync("/api/communications/calls", ValidCall("Sideways"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_call_with_no_SIP_call_id_is_refused_before_the_database()
    {
        // The Call-ID is what makes a repeat harmless. Without it the offline
        // queue could file the same call twice on every retry.
        var response = await ClientFor(UserRoles.Agent).PostAsJsonAsync(
            "/api/communications/calls",
            new { extension = "2001", direction = Directions.In, status = CommunicationStatuses.Answered });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_agent_reads_their_own_call_log_without_naming_themselves()
    {
        // There is no id in the route on purpose: the token decides whose calls
        // these are, so an agent cannot ask for somebody else's (A-50).
        var response = await ClientFor(UserRoles.Agent).GetAsync("/api/communications/mine");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_contact_history_is_open_to_any_signed_in_account()
    {
        // A-62: every agent sees a contact's full history from all agents.
        var response = await ClientFor(UserRoles.Agent)
            .GetAsync("/api/communications/by-contact/11111111-1111-1111-1111-111111111111");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Without_a_token_a_note_cannot_be_written()
    {
        var response = await factory.CreateClient().PutAsJsonAsync(
            "/api/communications/11111111-1111-1111-1111-111111111111/notes",
            new { notes = "Was on another call" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_agent_gets_past_the_door_to_write_a_note()
    {
        // A-41: a missed or rejected call takes a note instead of a
        // classification. Whose call it is and whether it is still editable are
        // decided past this point, against the database.
        var response = await ClientFor(UserRoles.Agent).PutAsJsonAsync(
            "/api/communications/11111111-1111-1111-1111-111111111111/notes",
            new { notes = "Was on another call" });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_note_longer_than_the_column_is_refused_before_the_database()
    {
        var response = await ClientFor(UserRoles.Agent).PutAsJsonAsync(
            "/api/communications/11111111-1111-1111-1111-111111111111/notes",
            new { notes = new string('x', 4001) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_agent_gets_past_the_door_to_note_a_call_by_its_SIP_id()
    {
        // The pop-up's route: an outbound call nobody picked up is noted as it
        // ends, keyed on the call because its server id is not known yet.
        var response = await ClientFor(UserRoles.Agent).PutAsJsonAsync(
            "/api/communications/by-call/notes",
            new { sipCallId = "abc123@pbx", extension = "2001", notes = "No answer, try after 6" });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_note_by_call_with_no_SIP_call_id_is_refused_before_the_database()
    {
        var response = await ClientFor(UserRoles.Agent).PutAsJsonAsync(
            "/api/communications/by-call/notes",
            new { extension = "2001", notes = "No answer" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private HttpClient ClientFor(string role)
    {
        var tokens = new TokenService(
            Options.Create(new JwtOptions { SigningKey = CallCenterApiFactory.SigningKey }),
            TimeProvider.System);

        var (token, _) = tokens.Issue(
            new User
            {
                Id = Guid.NewGuid(),
                Login = role.ToLowerInvariant(),
                DisplayName = role,
                Role = role,
                PasswordHash = "unused",
            },
            sessionId: null);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
