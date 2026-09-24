using System.Security.Claims;
using CallCenter.Server.Data;
using CallCenter.Server.Features.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace CallCenter.Server.Tests;

/// <summary>
/// Boots the real API in-process for the endpoint tests.
/// </summary>
/// <remarks>
/// Two things have to be supplied that production takes from the server
/// environment (runbook step 5): the JWT signing key and the SIP secret key.
/// Both are validated at startup, so without them the host would not come up at
/// all.
///
/// <b>The schema is created only when a database was actually provided.</b>
/// Most of this suite needs no PostgreSQL — it checks the door: that a
/// supervisor is refused, that a blank name is a 400, that a request is turned
/// away before any query. Those run anywhere. A handful reach the database
/// anyway, because the endpoint has to look something up before it can answer,
/// and <b>they were the ones failing in CI with a 500 where the test wanted a
/// 404</b>. They passed on a developer's laptop purely because Docker happened
/// to be running there, which is the worst way for a test to pass.
///
/// So: no connection string in the environment, no migrations, and the suite
/// behaves exactly as it always has. A connection string present — which is
/// what the CI workflow now supplies alongside a real PostgreSQL service — and
/// the schema is built before the first test, so those endpoints answer for
/// real instead of failing to connect.
/// </remarks>
public class CallCenterApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The signing key these tests issue and validate tokens with.</summary>
    public const string SigningKey = "test-only-signing-key-0123456789abcdefghij";

    /// <summary>
    /// Whether a database was handed to this run, by the same configuration key
    /// production uses. Set by the CI workflow beside its PostgreSQL service;
    /// unset on a laptop, where the suite stays database-free.
    /// </summary>
    public static bool HasDatabase =>
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("ConnectionStrings__Default"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment("Testing")
            .UseSetting("Database:MigrateOnStartup", HasDatabase ? "true" : "false")
            .UseSetting("Jwt:SigningKey", SigningKey)
            .UseSetting("Security:SipSecretKey", "test-only-sip-secret-key")
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<AccountTokenCheck>();
                services.AddScoped<AccountTokenCheck, TokensForMadeUpAccounts>();
            });
    }

    /// <summary>
    /// Puts the real <see cref="AccountTokenCheck"/> back, for a host that signs
    /// real accounts in. <see cref="TestData.Client"/> uses it, so every
    /// database test goes through the check that production runs.
    /// </summary>
    public static void UseRealTokenCheck(IServiceCollection services)
    {
        services.RemoveAll<AccountTokenCheck>();
        services.AddScoped<AccountTokenCheck>();
    }

    /// <summary>
    /// Accepts any correctly signed token. <b>Only for the door tests</b>, which
    /// mint tokens for accounts that exist nowhere, to check a policy (a
    /// supervisor is 403, a blank name is 400) without a database. The real
    /// check would turn every one of them into a 401 before the policy ran.
    /// Whether a token outlives a password reset is tested with real accounts,
    /// in <see cref="LoginTests"/>.
    /// </summary>
    private sealed class TokensForMadeUpAccounts(CallCenterDbContext db, ILogger<AccountTokenCheck> logger)
        : AccountTokenCheck(db, logger)
    {
        public override Task<bool> IsCurrentAsync(ClaimsPrincipal principal, CancellationToken ct) =>
            Task.FromResult(true);
    }
}
