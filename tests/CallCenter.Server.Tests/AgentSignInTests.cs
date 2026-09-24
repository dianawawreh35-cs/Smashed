using System.Net;
using System.Net.Http.Json;
using CallCenter.Server.Data;
using CallCenter.Server.Data.Entities;
using CallCenter.Server.Features.Auth;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// An agent's session and SIP details go to the Agent App, and only to it
/// (A-05, N-05). Against a real database, because the difference is a row in
/// <c>agent_sessions</c> and a secret decrypted from <c>users</c>.
/// </summary>
[Collection(ApiCollection.Name)]
public class AgentSignInTests(CallCenterApiFactory factory)
{
    private const string Password = "correct horse battery";

    [DatabaseFact]
    public async Task An_agent_signing_in_to_the_web_app_is_given_no_session_and_no_SIP_details()
    {
        var agent = await CreateAgentAsync();

        // What the web app sends: no laptop id.
        var response = await Client().PostAsJsonAsync(
            "/api/auth/login", new { login = agent.Login, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the web app needs the role to refuse the agent");
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        body!.User.Role.Should().Be(UserRoles.Agent);
        body.SessionId.Should().BeNull("nothing would ever close it: the browser refuses the agent and moves on");
        body.Extensions.Should().BeNull("the SIP secret is for the softphone, not a browser");
        (await SessionCountAsync(agent.Id)).Should().Be(0);
    }

    [DatabaseFact]
    public async Task An_agent_signing_in_to_the_Agent_App_is_given_a_session_and_SIP_details()
    {
        var agent = await CreateAgentAsync();

        var response = await Client().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(agent.Login, Password, LaptopId: "LAPTOP-TEST", AppVersion: "test"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<LoginResponse>();

        body!.SessionId.Should().NotBeNull();
        body.Extensions.Should().NotBeNull();
        body.Extensions!.Extension.Should().Be(agent.Extension);
        (await SessionCountAsync(agent.Id)).Should().Be(1);
    }

    /// <summary>
    /// A PBX address from configuration, for when the database's <c>pbx.host</c>
    /// is blank: without one the server rightly withholds the extension, and the
    /// second test could not tell that apart from the rule under test.
    /// </summary>
    private HttpClient Client() =>
        factory.WithWebHostBuilder(b => b.UseSetting("Sip:Server", "192.0.2.10")).CreateClient();

    private async Task<User> CreateAgentAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        var secrets = scope.ServiceProvider.GetRequiredService<ISipSecretProtector>();

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var agent = new User
        {
            Login = $"test-agent-{suffix}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(Password),
            DisplayName = $"Test agent {suffix}",
            Role = UserRoles.Agent,
            Extension = $"9{Random.Shared.Next(100, 999)}",
            SipSecret = secrets.Protect("sip-secret"),
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.Users.Add(agent);
        await db.SaveChangesAsync();
        return agent;
    }

    private async Task<int> SessionCountAsync(Guid userId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
        return await db.AgentSessions.CountAsync(s => s.UserId == userId);
    }
}
