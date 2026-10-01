using System.Net.Http.Json;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Contracts.Users;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// The Users page shows each agent's Agent App version, from their latest
/// sign-in to it (S-42). Against a real database.
/// </summary>
[Collection(ApiCollection.Name)]
public class UserAppVersionTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task The_list_shows_the_version_of_each_agent_s_latest_app_sign_in()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var updated = await data.CreateUserAsync();
        var never = await data.CreateUserAsync();

        await SignInWithAsync(updated, "0.8.1");
        await SignInWithAsync(updated, "0.8.2");

        var users = (await supervisor.GetFromJsonAsync<List<UserDto>>("/api/users"))!;

        var row = users.Single(u => u.Id == updated.Id);
        row.AppVersion.Should().Be("0.8.2", "the latest sign-in is the one that counts");
        row.AppSignedInAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));

        users.Single(u => u.Id == never.Id).AppVersion.Should().BeNull("this agent never signed in to the app");
    }

    [DatabaseFact]
    public async Task A_single_account_and_a_web_sign_in_carry_it_too()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var agent = await data.CreateUserAsync();

        await SignInWithAsync(agent, "0.8.1");

        // The web app sends no laptop id and no version, and opens no session:
        // it must not wipe out what the Agent App said.
        (await data.Client().PostAsJsonAsync("/api/auth/login", new LoginRequest(agent.Login, TestData.Password)))
            .IsSuccessStatusCode.Should().BeTrue();

        var one = (await supervisor.GetFromJsonAsync<UserDto>($"/api/users/{agent.Id}"))!;
        one.AppVersion.Should().Be("0.8.1");
    }

    private async Task SignInWithAsync(Data.Entities.User agent, string version)
    {
        var response = await data.Client().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(agent.Login, TestData.Password, TestData.LaptopId, version));
        response.IsSuccessStatusCode.Should().BeTrue();
    }
}
