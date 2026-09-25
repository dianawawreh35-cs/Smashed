using System.Security.Claims;
using CallCenter.Server.Data;
using CallCenter.Server.Features.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

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
public class CallCenterApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    /// <summary>
    /// Before the first test: remove whatever an earlier run left, so a run that
    /// crashed before its own sweep is harmless (<see cref="TestSweeper"/>).
    /// </summary>
    public Task InitializeAsync()
    {
        RefuseTheDevelopmentDatabase();
        return SweepAsync();
    }

    /// <summary>
    /// Stops the run before any test if it was pointed at the database the
    /// apps use on this machine.
    /// </summary>
    /// <remarks>
    /// DEVELOPING.md §5 has always said to use <c>callcenter_test</c>. On 25 Sep
    /// the suite was run against <c>callcenter</c> anyway, and it filled the
    /// supervisor's screens with test agents and channels, published a form
    /// agents were then asked, and ran the real retention job over real
    /// recordings. A rule in a document did not hold, so it is a check. CI's own
    /// throwaway database is also called <c>callcenter</c>; GitHub sets
    /// <c>CI=true</c>, which is how it is told apart.
    /// </remarks>
    private static void RefuseTheDevelopmentDatabase()
    {
        if (!HasDatabase || Environment.GetEnvironmentVariable("CI") == "true")
        {
            return;
        }

        var database = new Npgsql.NpgsqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("ConnectionStrings__Default")).Database;

        if (string.Equals(database, "callcenter", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The database tests were pointed at 'callcenter', the database the apps use. "
                + "Run them against 'callcenter_test' (DEVELOPING.md, section 5).");
        }
    }

    /// <summary>Where this run's recordings are written. Removed with the run.</summary>
    public static readonly string RecordingsPath =
        Path.Combine(Path.GetTempPath(), "callcenter-tests", Guid.NewGuid().ToString("N"));

    /// <summary>After the last test: remove every row this run made.</summary>
    async Task IAsyncLifetime.DisposeAsync()
    {
        await SweepAsync();
        DeleteRecordings();
        await base.DisposeAsync();
    }

    private static void DeleteRecordings()
    {
        try
        {
            if (Directory.Exists(RecordingsPath))
            {
                Directory.Delete(RecordingsPath, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still open is left for the OS's temp cleaner; never fail a run over it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private async Task SweepAsync()
    {
        if (!HasDatabase)
        {
            return;
        }

        await using var scope = Services.CreateAsyncScope();
        await TestSweeper.SweepAsync(scope.ServiceProvider.GetRequiredService<CallCenterDbContext>());
    }

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
            // A folder of this run's own for the audio the recording tests
            // upload, deleted when the run ends. It was the test project's bin
            // folder, where 70 WAV files had piled up by 25 Sep.
            .UseSetting("Recordings:Path", RecordingsPath)
            .ConfigureTestServices(services =>
            {
                services.RemoveAll<AccountTokenCheck>();
                services.AddScoped<AccountTokenCheck, TokensForMadeUpAccounts>();
            });
    }

    private WebApplicationFactory<Program>? _realAccounts;

    /// <summary>
    /// The host the database tests sign real accounts in to. It runs the real
    /// <see cref="AccountTokenCheck"/>, so every database test goes through the
    /// check production runs, and it has a PBX address, so an agent's sign-in
    /// returns an extension.
    /// </summary>
    /// <remarks>
    /// <b>One, shared by every test.</b> It was created per call once, and every
    /// host is a server with its own connection pool that nothing disposed. A
    /// suite that starts sixty of them holds connections the database needs for
    /// other things. That is the same shortage <c>ConnectionPool</c> guards
    /// against in production.
    /// </remarks>
    public WebApplicationFactory<Program> RealAccounts => _realAccounts ??= WithWebHostBuilder(b => b
        .UseSetting("Sip:Server", "192.0.2.10")
        .ConfigureTestServices(services =>
        {
            services.RemoveAll<AccountTokenCheck>();
            services.AddScoped<AccountTokenCheck>();
        }));

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
