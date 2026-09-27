using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using CallCenter.Shared.Contracts.Users;
using FluentAssertions;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// A supervisor's own password needs the current one to change; anybody
/// else's does not (S-42, 27 Sep review). Against a real database.
/// </summary>
[Collection(ApiCollection.Name)]
public class OwnPasswordTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    private const string NewPassword = "a brand new password";

    [DatabaseFact]
    public async Task Your_own_password_needs_the_current_one_and_a_wrong_one_changes_nothing()
    {
        var me = await data.CreateUserAsync(UserRoles.Supervisor);
        var (client, _) = await data.SignInAsync(me);

        var without = await client.PostAsJsonAsync($"/api/users/{me.Id}/password", new ResetPasswordRequest(NewPassword));
        without.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(without)).Should().Be("current_password_required");

        var wrong = await client.PostAsJsonAsync($"/api/users/{me.Id}/password", new ResetPasswordRequest(NewPassword, "not it"));
        wrong.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await CodeAsync(wrong)).Should().Be("current_password_wrong");
        (await SignInAsync(me.Login, TestData.Password)).Should().Be(HttpStatusCode.OK, "nothing was changed");

        var right = await client.PostAsJsonAsync($"/api/users/{me.Id}/password", new ResetPasswordRequest(NewPassword, TestData.Password));
        right.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SignInAsync(me.Login, NewPassword)).Should().Be(HttpStatusCode.OK);
    }

    [DatabaseFact]
    public async Task Another_account_s_password_is_set_without_the_old_one()
    {
        var (supervisor, _) = await data.SignInAsync(await data.CreateUserAsync(UserRoles.Supervisor));
        var agent = await data.CreateUserAsync();

        var response = await supervisor.PostAsJsonAsync($"/api/users/{agent.Id}/password", new ResetPasswordRequest(NewPassword));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await SignInAsync(agent.Login, NewPassword, TestData.LaptopId)).Should().Be(HttpStatusCode.OK);
    }

    private async Task<HttpStatusCode> SignInAsync(string login, string password, string? laptop = null) =>
        (await data.Client().PostAsJsonAsync("/api/auth/login", new LoginRequest(login, password, laptop, "test"))).StatusCode;

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("code").GetString();
    }
}
