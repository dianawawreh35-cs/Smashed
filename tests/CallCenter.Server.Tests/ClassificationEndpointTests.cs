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
/// Who may classify a call, and who decides what the form asks (A-40 to A-43,
/// S-40).
/// </summary>
/// <remarks>
/// This is the data every report counts. A form an agent can rewrite is a report
/// nobody can trust, and a classification an agent can change a month later is a
/// record that says whatever was convenient — so the door matters as much as the
/// content. These run without PostgreSQL and stop at authorization and
/// validation.
/// </remarks>
[Collection(ApiCollection.Name)]
public class ClassificationEndpointTests(CallCenterApiFactory factory)
{
    private const string SomeType = "/api/classifications/types/11111111-1111-1111-1111-111111111111";

    public static TheoryData<string, string> SupervisorOnlyEndpoints => new()
    {
        { "PUT", "/api/classifications/form" },
        { "POST", "/api/classifications/types" },
        { "PUT", SomeType },
        { "DELETE", SomeType },
    };

    [Theory]
    [MemberData(nameof(SupervisorOnlyEndpoints))]
    public async Task Without_a_token_the_form_cannot_be_changed(string method, string path)
    {
        var response = await factory.CreateClient().SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(SupervisorOnlyEndpoints))]
    public async Task An_agent_cannot_change_the_form_or_its_types(string method, string path)
    {
        // S-40: the supervisor defines the form. An agent who could edit it
        // could rename "Complaint" out of existence and every report with it.
        var response = await ClientFor(UserRoles.Agent).SendAsync(
            new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("/api/classifications/form")]
    [InlineData("/api/classifications/types")]
    public async Task An_agent_may_read_the_form(string path)
    {
        // A-40: without this an agent cannot classify anything at all.
        var response = await ClientFor(UserRoles.Agent).GetAsync(path);

        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Classifying_needs_a_signed_in_user()
    {
        var response = await factory.CreateClient().PutAsJsonAsync(
            "/api/classifications/11111111-1111-1111-1111-111111111111",
            new { typeId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_classification_with_no_type_is_refused_before_the_database()
    {
        // The type is what every report groups by; a classification without one
        // describes nothing.
        var response = await ClientFor(UserRoles.Agent).PutAsJsonAsync(
            "/api/classifications/11111111-1111-1111-1111-111111111111",
            new { notes = "no type given" });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_negative_order_value_is_refused()
    {
        var response = await ClientFor(UserRoles.Agent).PutAsJsonAsync(
            "/api/classifications/11111111-1111-1111-1111-111111111111",
            new { typeId = Guid.NewGuid(), orderValue = -5 });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_offline_route_needs_a_call_reference_and_an_extension()
    {
        // A-04: the Agent App queues a classification for a call the server has
        // not seen. Without both keys it could land on the wrong call.
        var response = await ClientFor(UserRoles.Agent).PutAsJsonAsync(
            "/api/classifications/by-call",
            new { sipCallId = "", extension = "", classification = new { typeId = Guid.NewGuid() } });

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
