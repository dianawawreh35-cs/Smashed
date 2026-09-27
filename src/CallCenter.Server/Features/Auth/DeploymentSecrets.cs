namespace CallCenter.Server.Features.Auth;

/// <summary>
/// Refuses to start with a signing or encryption key that is public (F-13 of
/// the 27 Sep review).
/// </summary>
/// <remarks>
/// <c>deploy/.env.example</c> ships <c>change-me-to-a-long-random-string</c>
/// for both keys. It is 33 characters, so it passed the JWT key's 32-character
/// minimum, and the SIP key took anything that was not blank: an installer who
/// forgot two lines ran with keys anyone can read in git, and anyone with them
/// can mint a supervisor's token or read every extension's password out of a
/// database dump. The development keys in <c>appsettings.Development.json</c>
/// are just as public, so outside Development they are refused too.
/// </remarks>
public static class DeploymentSecrets
{
    /// <summary>The keys <c>appsettings.Development.json</c> carries. Public, and fine only there.</summary>
    public static readonly IReadOnlyList<string> DevelopmentKeys =
    [
        "dev-only-signing-key-not-for-production-0123456789",
        "dev-only-sip-secret-key-not-for-production",
    ];

    private static readonly (string Path, string EnvName)[] Keys =
    [
        ("Jwt:SigningKey", "JWT_SECRET"),
        (SipSecretProtector.KeyConfigurationPath, "SIP_SECRET_KEY"),
    ];

    /// <summary>Throws, naming the setting to fill in, if either key is a placeholder or a published one.</summary>
    public static void Check(IConfiguration configuration, bool isDevelopment)
    {
        foreach (var (path, envName) in Keys)
        {
            if (Problem(configuration[path], isDevelopment) is { } problem)
            {
                throw new InvalidOperationException(
                    $"'{path}' {problem}. Set {envName} in /opt/callcenter/.env to a value of your own, "
                    + "made with `openssl rand -base64 48`, and start again (runbook step 5).");
            }
        }
    }

    /// <summary>Why <paramref name="value"/> will not do, or null. A blank one is left to the checks that already refuse it.</summary>
    public static string? Problem(string? value, bool isDevelopment)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (value.Contains("change-me", StringComparison.OrdinalIgnoreCase))
        {
            return "is still the example value from .env.example";
        }

        if (!isDevelopment && DevelopmentKeys.Contains(value.Trim()))
        {
            return "is the development key, which is in the repository for anyone to read";
        }

        return null;
    }
}
