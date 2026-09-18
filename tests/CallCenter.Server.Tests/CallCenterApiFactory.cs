using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CallCenter.Server.Tests;

/// <summary>
/// Boots the real API in-process for tests that need no PostgreSQL.
/// </summary>
/// <remarks>
/// Two things have to be supplied that production takes from the server
/// environment (runbook step 5): the JWT signing key and the SIP secret key.
/// Both are validated at startup, so without them the host would not come up at
/// all. Migrations are turned off for the same reason as always — CI has no
/// database service.
/// </remarks>
public class CallCenterApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The signing key these tests issue and validate tokens with.</summary>
    public const string SigningKey = "test-only-signing-key-0123456789abcdefghij";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder
            .UseEnvironment("Testing")
            .UseSetting("Database:MigrateOnStartup", "false")
            .UseSetting("Jwt:SigningKey", SigningKey)
            .UseSetting("Security:SipSecretKey", "test-only-sip-secret-key");
    }
}
