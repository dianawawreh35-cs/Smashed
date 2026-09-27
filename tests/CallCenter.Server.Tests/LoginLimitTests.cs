using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Shared.Contracts.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The sign-in limits (F-12): too many attempts answer 429 with
/// <c>too_many_attempts</c>, before any password is checked.
/// </summary>
/// <remarks>
/// Each test starts a host of its own with low limits; the shared one has them
/// raised so the rest of the suite can sign in freely. The attempts before the
/// limit fail however they fail (401 with a database, 500 without); what is
/// checked is that they are not 429 and the next one is.
/// </remarks>
[Collection(ApiCollection.Name)]
public class LoginLimitTests(CallCenterApiFactory factory)
{
    [Fact]
    public async Task The_same_login_is_refused_after_its_limit_from_any_address()
    {
        await using var host = Host(perAddress: 1000, perLogin: 3);
        var client = host.CreateClient();
        var login = $"guess-{Guid.NewGuid():N}"[..20];

        for (var i = 0; i < 3; i++)
        {
            (await AttemptAsync(client, login)).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, $"attempt {i + 1} is within the limit");
        }

        await ShouldBeRefusedAsync(await AttemptAsync(client, login));
        await ShouldBeRefusedAsync(await AttemptAsync(client, login.ToUpperInvariant()), "the login is matched without case");
        (await AttemptAsync(client, $"{login}-other")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, "another login has its own count");
    }

    [Fact]
    public async Task One_address_is_refused_after_its_limit_whatever_logins_it_tries()
    {
        await using var host = Host(perAddress: 4, perLogin: 1000);
        var client = host.CreateClient();

        for (var i = 0; i < 4; i++)
        {
            (await AttemptAsync(client, $"someone-{i}")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        }

        await ShouldBeRefusedAsync(await AttemptAsync(client, "someone-else"));
    }

    private WebApplicationFactory<Program> Host(int perAddress, int perLogin) => factory.WithWebHostBuilder(b => b
        .UseSetting("Auth:LoginLimits:PerAddressPerMinute", perAddress.ToString())
        .UseSetting("Auth:LoginLimits:PerLoginPerMinute", perLogin.ToString()));

    private static Task<HttpResponseMessage> AttemptAsync(HttpClient client, string login) =>
        client.PostAsJsonAsync("/api/auth/login", new LoginRequest(login, "wrong password"));

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, string because = "")
    {
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests, because);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("too_many_attempts");
    }
}
