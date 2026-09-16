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
/// Who may reach account management (S-42). This is where agent SIP credentials
/// are set, so the door matters as much as what is behind it.
/// </summary>
/// <remarks>
/// The behaviour behind the door reads the <c>users</c> table and belongs to the
/// database-backed tests; these run without PostgreSQL, so they stop at
/// authorization. An agent token reaching a 403 never touches the database.
/// </remarks>
[Collection(ApiCollection.Name)]
public class UsersEndpointTests(CallCenterApiFactory factory)
{
    public static TheoryData<string, string> ProtectedEndpoints => new()
    {
        { "GET", "/api/users" },
        { "POST", "/api/users" },
        { "PUT", "/api/users/11111111-1111-1111-1111-111111111111" },
        { "PUT", "/api/users/11111111-1111-1111-1111-111111111111/extensions" },
        { "POST", "/api/users/11111111-1111-1111-1111-111111111111/password" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task Without_a_token_every_endpoint_is_401(string method, string path)
    {
        var client = factory.CreateClient();

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task An_agent_token_is_403_on_every_endpoint(string method, string path)
    {
        // An agent must not be able to read the account list, let alone set
        // another agent's SIP credentials (S-42, N-05).
        var client = ClientFor(UserRoles.Agent);

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_supervisor_token_gets_past_authorization()
    {
        var client = ClientFor(UserRoles.Supervisor);

        var response = await client.GetAsync("/api/users");

        // Past the door, the request reaches the controller and then fails on
        // the database these tests deliberately do without. Anything other than
        // 401 or 403 shows authorization let it through.
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
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
