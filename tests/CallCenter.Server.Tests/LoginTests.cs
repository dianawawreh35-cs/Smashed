using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CallCenter.Server.Data.Seed;
using CallCenter.Shared;
using CallCenter.Shared.Contracts.Auth;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Signing in, against a real database (A-01, A-05, S-01, N-05).
/// </summary>
/// <remarks>
/// Every login once answered 500 while 176 tests stayed green, because none of
/// them posted a login. These post real ones, to the endpoint both apps use,
/// with accounts that exist in <c>users</c>.
/// </remarks>
[Collection(ApiCollection.Name)]
public class LoginTests(CallCenterApiFactory factory)
{
    private readonly TestData data = new(factory);

    [DatabaseFact]
    public async Task An_agent_signing_in_to_the_Agent_App_is_given_a_session_and_SIP_details()
    {
        var agent = await data.CreateUserAsync(UserRoles.Agent);

        var response = await data.Client().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(agent.Login, TestData.Password, TestData.LaptopId, "test"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        body.AccessToken.Should().NotBeNullOrEmpty();
        body.User.Role.Should().Be(UserRoles.Agent);
        body.SessionId.Should().NotBeNull();
        body.Extensions.Should().NotBeNull();
        body.Extensions!.Extension.Should().Be(agent.Extension);
        body.Extensions.Secret.Should().Be("sip-secret", "the app registers with it; it is stored encrypted");

        var session = await data.QueryAsync(db =>
            db.AgentSessions.SingleAsync(s => s.UserId == agent.Id));
        session.Id.Should().Be(body.SessionId!.Value);
        session.LaptopId.Should().Be(TestData.LaptopId);
        session.LoggedOutAt.Should().BeNull();
    }

    [DatabaseFact]
    public async Task An_agent_signing_in_to_the_web_app_is_given_no_session_and_no_SIP_details()
    {
        var agent = await data.CreateUserAsync(UserRoles.Agent);

        // What the web app sends: no laptop id.
        var response = await data.Client().PostAsJsonAsync(
            "/api/auth/login", new { login = agent.Login, password = TestData.Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the web app needs the role to refuse the agent");
        var body = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        body.User.Role.Should().Be(UserRoles.Agent);
        body.SessionId.Should().BeNull("nothing would ever close it: the browser refuses the agent and moves on");
        body.Extensions.Should().BeNull("the SIP secret is for the softphone, not a browser");
        (await SessionCountAsync(agent.Id)).Should().Be(0);
    }

    [DatabaseFact]
    public async Task A_supervisor_is_signed_in_with_no_session_and_no_SIP_details()
    {
        var supervisor = await data.CreateUserAsync(UserRoles.Supervisor);

        var response = await data.Client().PostAsJsonAsync(
            "/api/auth/login", new { login = supervisor.Login, password = TestData.Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<LoginResponse>())!;

        body.User.Role.Should().Be(UserRoles.Supervisor);
        body.SessionId.Should().BeNull("only agents have sessions: they are what the idle timer closes");
        body.Extensions.Should().BeNull("a supervisor has no phone");
        (await SessionCountAsync(supervisor.Id)).Should().Be(0);
    }

    [DatabaseFact]
    public async Task The_login_is_matched_whatever_the_case_it_is_typed_in()
    {
        var agent = await data.CreateUserAsync(UserRoles.Agent);

        var response = await data.Client().PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(agent.Login.ToUpperInvariant(), TestData.Password, TestData.LaptopId));

        response.StatusCode.Should().Be(HttpStatusCode.OK, "agents type their name in whatever case they like");
    }

    [DatabaseFact]
    public async Task The_wrong_password_is_refused_as_invalid_credentials()
    {
        var agent = await data.CreateUserAsync(UserRoles.Agent);

        var response = await data.Client().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(agent.Login, "not the password", TestData.LaptopId));

        await ShouldBeRefusedAsync(response, LoginErrorCodes.InvalidCredentials);
        (await SessionCountAsync(agent.Id)).Should().Be(0);
    }

    [DatabaseFact]
    public async Task An_unknown_login_gets_the_same_answer_as_a_wrong_password()
    {
        // Anything else would tell a stranger at a laptop which names exist.
        var response = await data.Client().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest($"nobody-{Guid.NewGuid():N}", TestData.Password));

        await ShouldBeRefusedAsync(response, LoginErrorCodes.InvalidCredentials);
    }

    [DatabaseFact]
    public async Task A_disabled_account_is_told_so_when_the_password_is_right()
    {
        // Told apart from a wrong password on purpose: the agent at the laptop
        // cannot fix it, and "wrong password" would send them round in circles.
        var agent = await data.CreateUserAsync(UserRoles.Agent, isActive: false);

        var response = await data.Client().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(agent.Login, TestData.Password, TestData.LaptopId));

        await ShouldBeRefusedAsync(response, LoginErrorCodes.AccountDisabled);
        (await SessionCountAsync(agent.Id)).Should().Be(0);
    }

    [DatabaseFact]
    public async Task A_disabled_account_with_the_wrong_password_does_not_say_it_is_disabled()
    {
        // Otherwise the refusal would confirm the name exists to anyone guessing.
        var agent = await data.CreateUserAsync(UserRoles.Agent, isActive: false);

        var response = await data.Client().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(agent.Login, "not the password", TestData.LaptopId));

        await ShouldBeRefusedAsync(response, LoginErrorCodes.InvalidCredentials);
    }

    [DatabaseFact]
    public async Task The_token_a_login_returns_is_accepted_and_names_the_account()
    {
        var agent = await data.CreateUserAsync(UserRoles.Agent);
        var (client, _) = await data.SignInAsync(agent);

        var me = await client.GetFromJsonAsync<CurrentUserDto>("/api/auth/me");

        me!.Id.Should().Be(agent.Id);
        me.Login.Should().Be(agent.Login);
        me.Role.Should().Be(UserRoles.Agent);

        var lastLogin = await data.QueryAsync(db =>
            db.Users.Where(u => u.Id == agent.Id).Select(u => u.LastLoginAt).SingleAsync());
        lastLogin.Should().NotBeNull();
    }

    [DatabaseFact]
    public async Task Logging_out_closes_the_session_the_login_opened()
    {
        var agent = await data.CreateUserAsync(UserRoles.Agent);
        var (client, login) = await data.SignInAsync(agent);

        var response = await client.PostAsJsonAsync(
            "/api/auth/logout", new LogoutRequest(login.SessionId!.Value, LogoutReasons.Manual));

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var session = await data.QueryAsync(db =>
            db.AgentSessions.SingleAsync(s => s.Id == login.SessionId));
        session.LoggedOutAt.Should().NotBeNull();
        session.LogoutReason.Should().Be(LogoutReasons.Manual);
    }

    [DatabaseFact]
    public async Task Reset_password_is_the_way_back_in_for_a_locked_out_disabled_account()
    {
        // The 19 September lockout: a forgotten password on a disabled account,
        // and no screen that could fix it. The command must leave an account
        // that signs in with the new password, and only the new one.
        var supervisor = await data.CreateUserAsync(UserRoles.Supervisor, isActive: false);

        var exitCode = await ResetPasswordCommand.RunAsync(
            factory.Services,
            ["reset-password", "--user", supervisor.Login.ToUpperInvariant(), "--password", "NewPass!2026"]);

        exitCode.Should().Be(0);

        var withNew = await data.Client().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(supervisor.Login, "NewPass!2026"));
        withNew.StatusCode.Should().Be(HttpStatusCode.OK, "the account was re-enabled with its new password");

        var withOld = await data.Client().PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(supervisor.Login, TestData.Password));
        await ShouldBeRefusedAsync(withOld, LoginErrorCodes.InvalidCredentials);
    }

    [DatabaseFact]
    public async Task Reset_password_closes_every_session_the_account_had_open()
    {
        var agent = await data.CreateUserAsync(UserRoles.Agent);
        var (_, first) = await data.SignInAsync(agent);
        var (_, second) = await data.SignInAsync(agent);

        var exitCode = await ResetPasswordCommand.RunAsync(
            factory.Services, ["reset-password", "--user", agent.Login, "--password", "NewPass!2026"]);

        exitCode.Should().Be(0);

        var sessions = await data.QueryAsync(db =>
            db.AgentSessions.Where(s => s.UserId == agent.Id).ToListAsync());
        sessions.Select(s => s.Id).Should().BeEquivalentTo([first.SessionId!.Value, second.SessionId!.Value]);
        sessions.Should().OnlyContain(s => s.LoggedOutAt != null && s.LogoutReason == LogoutReasons.PasswordReset);
    }

    [DatabaseFact]
    public async Task Reset_password_for_a_login_that_does_not_exist_changes_nothing_and_fails()
    {
        var exitCode = await ResetPasswordCommand.RunAsync(
            factory.Services,
            ["reset-password", "--user", $"nobody-{Guid.NewGuid():N}", "--password", "NewPass!2026"]);

        exitCode.Should().Be(1);
    }

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, string code)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    private Task<int> SessionCountAsync(Guid userId) =>
        data.QueryAsync(db => db.AgentSessions.CountAsync(s => s.UserId == userId));
}
