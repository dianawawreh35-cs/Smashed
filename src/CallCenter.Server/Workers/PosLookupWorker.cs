using CallCenter.Server.Features.Pos;
using Microsoft.Extensions.Options;

namespace CallCenter.Server.Workers;

/// <summary>
/// Runs the POS customer lookup (A-67) every
/// <c>pos.lookup.interval_minutes</c>, five by default.
/// </summary>
/// <remarks>
/// <b>Does nothing without a token.</b> It says so once at startup and stops,
/// so a server that was never given one, and the test host, never make a
/// request.
///
/// <b>It wakes often and usually does nothing.</b> Every <see cref="Tick"/> it
/// asks <see cref="PosCustomerSync.RunIfDueAsync"/>, which reads the setting
/// and runs only once the interval has passed. So a new interval on the
/// settings screen takes effect within half a minute, with no restart.
///
/// <b>It cannot take the server down.</b> As with the retention job, every run
/// is wrapped: a failure is logged and the timer carries on. The database is
/// reached through a scope of its own, since the context is scoped and this
/// service is not.
/// </remarks>
public class PosLookupWorker(
    IServiceScopeFactory scopes,
    IOptions<PosLookupOptions> options,
    ILogger<PosLookupWorker> logger) : BackgroundService
{
    /// <summary>
    /// How long after startup the first run happens: after the migrations and
    /// the first sign-ins, soon enough that a restart does not leave a gap.
    /// </summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    /// <summary>How often it checks whether a run is due.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("The POS customer lookup is off: no PosLookup:Token is set.");
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Tick);

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunAsync(stoppingToken);

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<PosCustomerSync>().RunIfDueAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The server is shutting down. Not a failure.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The POS customer lookup run failed. It will be tried again at the next interval.");
        }
    }
}
