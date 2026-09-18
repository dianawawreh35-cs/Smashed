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
/// Who may change a VIP or Blocked flag, and what is refused before the
/// database is touched (S-45).
/// </summary>
/// <remarks>
/// The split is the point of this file. S-45 says agents see the flags but
/// cannot change them, so reading must let an agent through — the pop-up needs
/// it (A-16, A-17) — while every write must stop them. These run without
/// PostgreSQL, so they cover the door and the refusals that answer before any
/// query.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ContactFlagsEndpointTests(CallCenterApiFactory factory)
{
    private const string SomeContact = "/api/contacts/11111111-1111-1111-1111-111111111111";

    /// <summary>Writes, and the history that names who blocked whom.</summary>
    public static TheoryData<string, string> SupervisorOnlyEndpoints => new()
    {
        { "PUT", $"{SomeContact}/flags" },
        { "POST", "/api/contacts/flags/by-number" },
        { "GET", $"{SomeContact}/flags/history" },
    };

    /// <summary>Reads an agent's own app depends on (A-16, A-17).</summary>
    public static TheoryData<string> AgentReadableEndpoints =>
    [
        "/api/contacts/flagged",
        "/api/contacts/blocked-numbers",
    ];

    [Theory]
    [MemberData(nameof(SupervisorOnlyEndpoints))]
    public async Task Without_a_token_a_flag_endpoint_is_401(string method, string path)
    {
        var client = factory.CreateClient();

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(AgentReadableEndpoints))]
    public async Task Without_a_token_even_the_block_list_is_401(string path)
    {
        var response = await factory.CreateClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(SupervisorOnlyEndpoints))]
    public async Task An_agent_token_is_403_on_every_write(string method, string path)
    {
        // S-45: agents see the flags, they do not set them. An agent clearing a
        // block on the customer who shouted at them must be impossible, not
        // merely absent from their screen.
        var client = ClientFor(UserRoles.Agent);

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(AgentReadableEndpoints))]
    public async Task An_agent_token_may_read_the_flags(string path)
    {
        // The pop-up shows a VIP badge (A-16) and the app rejects a blocked
        // caller from its cached copy of this list (A-17), so an agent has to
        // get through here.
        var response = await ClientFor(UserRoles.Agent).GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task VIP_and_Blocked_together_are_refused_before_the_database()
    {
        // The two ask the Agent App for opposite behaviour - show a badge, or
        // reject the call without ringing - so there is no sensible winner.
        var client = ClientFor(UserRoles.Supervisor);

        var response = await client.PutAsJsonAsync(
            $"{SomeContact}/flags",
            new { isVip = true, isBlocked = true, reason = "Both, somehow" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("vip_and_blocked");
    }

    [Fact]
    public async Task Setting_a_flag_with_no_reason_is_refused()
    {
        // S-45 asks for a reason. A block nobody can review later is a block
        // nobody dares remove.
        var client = ClientFor(UserRoles.Supervisor);

        var response = await client.PostAsJsonAsync(
            "/api/contacts/flags/by-number",
            new { number = "0599123456", isVip = false, isBlocked = true, reason = "   " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("reason_required");
    }

    [Fact]
    public async Task A_number_that_normalises_to_nothing_is_refused()
    {
        var client = ClientFor(UserRoles.Supervisor);

        var response = await client.PostAsJsonAsync(
            "/api/contacts/flags/by-number",
            new { number = "----", isVip = true, isBlocked = false, reason = "Regular" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeOf(response)).Should().Be("no_usable_number");
    }

    /// <summary>The <c>code</c> a client translates (A-80), not the English detail.</summary>
    private static async Task<string?> CodeOf(HttpResponseMessage response)
    {
        var problem = await response.Content.ReadFromJsonAsync<Dictionary<string, object>>();
        return problem?.TryGetValue("code", out var code) == true ? code.ToString() : null;
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
