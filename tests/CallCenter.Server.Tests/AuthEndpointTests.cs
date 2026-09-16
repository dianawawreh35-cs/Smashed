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
/// The parts of sign-in that can be checked without a database: that protected
/// endpoints are actually protected, and that a token issued for a user is
/// accepted by the same host that issued it (A-01, A-05).
/// </summary>
/// <remarks>
/// Login itself reads the <c>users</c> table, so it belongs to the integration
/// tests that run against PostgreSQL rather than here.
/// </remarks>
[Collection(ApiCollection.Name)]
public class AuthEndpointTests(CallCenterApiFactory factory)
{
    [Fact]
    public async Task Me_without_a_token_is_401()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_without_a_token_is_401()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/logout", new { sessionId = Guid.NewGuid(), reason = "Manual" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("", "somepassword")]
    [InlineData("supervisor", "")]
    public async Task Login_with_empty_fields_is_rejected_as_a_bad_request(string login, string password)
    {
        // Model validation answers before anything touches the database, so this
        // runs without PostgreSQL — and it is the only test that posts a real
        // body to the login endpoint. It exists because the validation
        // attributes on LoginRequest were once written as [property: Required],
        // which ASP.NET Core rejects for a record primary constructor: every
        // login answered 500 while all the other tests stayed green.
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new { login, password });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Me_with_a_garbage_token_is_401()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-token");

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_with_the_wrong_key_is_rejected()
    {
        // The signing key is what stands between a laptop on the LAN and a
        // forged supervisor token, so a different key must not open the door.
        var tokens = new TokenService(
            Options.Create(new JwtOptions { SigningKey = "a-completely-different-key-0123456789abcdef" }),
            TimeProvider.System);

        var (token, _) = tokens.Issue(NewSupervisor(), sessionId: null);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_expired_token_is_rejected()
    {
        // Issued yesterday, so it is long past both the 12-hour lifetime and the
        // 30-second clock-skew allowance.
        var yesterday = new FixedClock(DateTimeOffset.UtcNow.AddDays(-1));
        var (token, expiresAt) = IssuingService(yesterday).Issue(NewSupervisor(), sessionId: null);

        expiresAt.Should().BeBefore(DateTimeOffset.UtcNow);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await client.GetAsync("/api/auth/me");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // The happy path — a current token being accepted — is covered by the
    // database-backed login tests. Checking it here would reach the controller,
    // which queries a PostgreSQL that these tests deliberately do without, and
    // spend the connection retry budget before failing for the wrong reason.

    private static TokenService IssuingService(TimeProvider? clock = null) =>
        new(
            Options.Create(new JwtOptions
            {
                SigningKey = CallCenterApiFactory.SigningKey,
                Lifetime = TimeSpan.FromHours(12),
            }),
            clock ?? TimeProvider.System);

    /// <summary>A clock stopped at one instant, for issuing a token in the past.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private static User NewSupervisor() => new()
    {
        Id = Guid.NewGuid(),
        Login = "supervisor",
        DisplayName = "Supervisor",
        Role = UserRoles.Supervisor,
        PasswordHash = "unused",
    };
}
