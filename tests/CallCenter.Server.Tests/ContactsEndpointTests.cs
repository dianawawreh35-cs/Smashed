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
/// Who may reach the shared contact list (A-61, A-63).
/// </summary>
/// <remarks>
/// Unlike user management, this is open to agents on purpose: A-61 says every
/// agent sees every contact and A-63 says they create and edit them. These check
/// that an agent gets in and an unauthenticated caller does not. What the
/// endpoints then do reads the database and belongs to the integration tests.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ContactsEndpointTests(CallCenterApiFactory factory)
{
    public static TheoryData<string, string> Endpoints => new()
    {
        { "GET", "/api/contacts" },
        { "GET", "/api/contacts?q=ahmad" },
        { "GET", "/api/contacts/by-phone?number=0599123456" },
        { "GET", "/api/contacts/11111111-1111-1111-1111-111111111111" },
        { "POST", "/api/contacts" },
    };

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Without_a_token_every_endpoint_is_401(string method, string path)
    {
        var client = factory.CreateClient();

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task An_agent_token_is_not_refused(string method, string path)
    {
        // Contacts are shared, and agents are the ones who create them (A-61,
        // A-63) - so unlike /api/users, an agent must get past the door here.
        var client = ClientFor(UserRoles.Agent);

        var response = await client.SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_contact_with_no_phone_number_is_rejected_before_the_database()
    {
        // Validation answers first, so this runs without PostgreSQL. A contact
        // with no number could never be matched to a caller, which is most of
        // what contacts are for.
        var client = ClientFor(UserRoles.Agent);

        var response = await client.PostAsJsonAsync(
            "/api/contacts", new { name = "Ahmad", phones = Array.Empty<string>() });

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
