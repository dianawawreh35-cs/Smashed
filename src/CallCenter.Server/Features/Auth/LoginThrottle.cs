using System.Collections.Concurrent;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace CallCenter.Server.Features.Auth;

/// <summary>How often <c>POST /api/auth/login</c> may be tried (F-12). Section <c>Auth:LoginLimits</c>.</summary>
/// <remarks>
/// Dia, 27 Sep: ten a minute from one address and five a minute at one login.
/// The four agents may share one public address behind the router, so the
/// address limit is the looser one; the login limit is what stops someone
/// guessing one account's password.
/// </remarks>
public class LoginLimitOptions
{
    public const string SectionName = "Auth:LoginLimits";

    public int PerAddressPerMinute { get; set; } = 10;

    public int PerLoginPerMinute { get; set; } = 5;
}

/// <summary>
/// Limits sign-in attempts, per address and per login, with ASP.NET Core's
/// built-in rate limiter (F-12 of the 27 Sep review).
/// </summary>
/// <remarks>
/// Each attempt costs a BCrypt check, 100-200 ms of CPU, unknown logins
/// included, and nothing limited them: anyone on the LAN could guess passwords
/// freely, or send a few dozen at once and slow the API for every agent.
///
/// <b>Two limits, in two places.</b> The per-address one is the rate-limiting
/// middleware's, on the endpoint. The per-login one needs the login out of the
/// body, which the middleware runs too early to read, so the controller asks
/// <see cref="TryAcquire"/> with it; the limiter underneath is the same
/// library's. Both answer <b>429</b> with the code <c>too_many_attempts</c>,
/// as the other sign-in refusals carry a code.
///
/// Repeated failures at one login are logged as a warning, so a guessing run
/// is in the log whether or not it reached the limit.
/// </remarks>
public sealed class LoginThrottle : IDisposable
{
    public const string Policy = "login";

    public const string TooManyAttempts = "too_many_attempts";

    /// <summary>Failures at one login within this long count as repeated.</summary>
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(10);

    /// <summary>From the third failure in the window, each one is a warning.</summary>
    private const int WarnFrom = 3;

    private readonly PartitionedRateLimiter<string> _perLogin;
    private readonly ConcurrentDictionary<string, Failures> _failures = new(StringComparer.Ordinal);
    private readonly TimeProvider _clock;
    private readonly ILogger<LoginThrottle> _logger;

    public LoginThrottle(IOptions<LoginLimitOptions> options, TimeProvider clock, ILogger<LoginThrottle> logger)
    {
        _clock = clock;
        _logger = logger;
        var perMinute = Math.Max(1, options.Value.PerLoginPerMinute);

        _perLogin = PartitionedRateLimiter.Create<string, string>(login => RateLimitPartition.GetFixedWindowLimiter(
            login, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = perMinute,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
    }

    /// <summary>Case and spacing levelled, as the login itself is matched.</summary>
    private static string Key(string login) => login.Trim().ToLowerInvariant();

    /// <summary>Whether another attempt at <paramref name="login"/> may go ahead now.</summary>
    public bool TryAcquire(string login, string? address)
    {
        using var lease = _perLogin.AttemptAcquire(Key(login));
        if (lease.IsAcquired)
        {
            return true;
        }

        _logger.LogWarning(
            "Too many sign-in attempts at login {Login}; the last, from {Address}, was refused with 429",
            login, address ?? "an unknown address");
        return false;
    }

    /// <summary>Notes a failed attempt, and says so in the log once it is repeated.</summary>
    public void Failed(string login, string? address)
    {
        var now = _clock.GetUtcNow();
        var entry = _failures.AddOrUpdate(
            Key(login),
            _ => new Failures(now, 1),
            (_, f) => now - f.Since > FailureWindow ? new Failures(now, 1) : f with { Count = f.Count + 1 });

        if (entry.Count >= WarnFrom)
        {
            _logger.LogWarning(
                "{Count} failed sign-ins at login {Login} in the last {Minutes} minutes, the last from {Address}",
                entry.Count, login, (int)FailureWindow.TotalMinutes, address ?? "an unknown address");
        }

        // Never more than the logins tried recently.
        if (_failures.Count > 1000)
        {
            foreach (var (key, f) in _failures)
            {
                if (now - f.Since > FailureWindow)
                {
                    _failures.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>A success clears the count.</summary>
    public void Succeeded(string login) => _failures.TryRemove(Key(login), out _);

    public void Dispose() => _perLogin.Dispose();

    private sealed record Failures(DateTimeOffset Since, int Count);

    /// <summary>The 429 both limits answer with.</summary>
    public static ProblemDetails Refusal() => new()
    {
        Title = "Sign-in failed",
        Detail = "Too many sign-in attempts. Wait a minute and try again.",
        Status = StatusCodes.Status429TooManyRequests,
        Extensions = { ["code"] = TooManyAttempts },
    };

    /// <summary>The per-address limit, on the login endpoint only.</summary>
    public static void AddPolicy(RateLimiterOptions options, LoginLimitOptions limits)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        options.AddPolicy(Policy, context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = Math.Max(1, limits.PerAddressPerMinute),
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));

        options.OnRejected = async (context, ct) =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<LoginThrottle>>();
            logger.LogWarning(
                "Too many sign-in attempts from {Address}; refused with 429",
                context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "an unknown address");

            await context.HttpContext.Response.WriteAsJsonAsync(
                Refusal(), (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json", ct);
        };
    }
}
