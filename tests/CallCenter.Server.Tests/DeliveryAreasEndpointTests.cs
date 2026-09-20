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
/// Who may read delivery prices and who may change them (A-65, S-58).
/// </summary>
/// <remarks>
/// The split is the point: an agent quotes a price mid-call and must never be
/// able to alter one. Same shape as the VIP and Blocked flags. These run without
/// PostgreSQL, so they stop at the door.
/// </remarks>
[Collection(ApiCollection.Name)]
public class DeliveryAreasEndpointTests(CallCenterApiFactory factory)
{
    private const string SomeArea = "/api/delivery-areas/11111111-1111-1111-1111-111111111111";

    public static TheoryData<string, string> SupervisorOnlyEndpoints => new()
    {
        { "POST", "/api/delivery-areas" },
        { "PUT", SomeArea },
        { "DELETE", SomeArea },
        { "POST", "/api/delivery-areas/import" },
    };

    [Theory]
    [MemberData(nameof(SupervisorOnlyEndpoints))]
    public async Task Without_a_token_every_write_is_401(string method, string path)
    {
        var response = await factory.CreateClient().SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(SupervisorOnlyEndpoints))]
    public async Task An_agent_cannot_change_a_delivery_price(string method, string path)
    {
        // S-58: agents read these to quote a price. A price an agent can edit
        // is a price the restaurant does not control.
        var response = await ClientFor(UserRoles.Agent).SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_may_look_a_price_up()
    {
        // A-65: this is the whole point of the feature for an agent mid-call.
        var response = await ClientFor(UserRoles.Agent).GetAsync("/api/delivery-areas?q=كفر");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_may_read_the_branches()
    {
        // Every delivery row names one, so the lookup would be useless without.
        var response = await ClientFor(UserRoles.Agent).GetAsync("/api/branches");

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_area_with_no_name_is_refused_before_the_database()
    {
        var response = await ClientFor(UserRoles.Supervisor).PostAsJsonAsync(
            "/api/delivery-areas",
            new { name = "", branchId = Guid.NewGuid(), price = 10 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_negative_price_is_refused_before_the_database()
    {
        // Zero is valid and meant; negative is a typo, and the CHECK constraint
        // would otherwise reject it with something unreadable.
        var response = await ClientFor(UserRoles.Supervisor).PostAsJsonAsync(
            "/api/delivery-areas",
            new { name = "Somewhere", branchId = Guid.NewGuid(), price = -5 });

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
