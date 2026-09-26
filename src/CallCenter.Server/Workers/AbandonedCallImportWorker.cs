using CallCenter.Server.Features.Pbx;

namespace CallCenter.Server.Workers;

/// <summary>
/// Checks the PBX for abandoned calls (S-55) every
/// <c>pbx.calls.interval_minutes</c>, one minute by default.
/// </summary>
/// <remarks>
/// <b>It wakes often and usually does nothing.</b> Every
/// <see cref="Tick"/> it asks <see cref="AbandonedCallImport.RunIfDueAsync"/>,
/// which reads the settings and runs only when the import is set up and the
/// interval has passed. So a change on the settings screen - a new address, a
/// longer interval, the import switched off - needs no restart, and a server
/// that was never given a PBX login (the test host included) never makes a
/// request.
///
/// <b>It cannot take the server down.</b> As with the other jobs, every run is
/// wrapped: a failure is logged and the timer carries on. A PBX that cannot be
/// reached is not a failure here: the import records why, and the settings
/// screen shows it.
/// </remarks>
public class AbandonedCallImportWorker(
    IServiceScopeFactory scopes,
    ILogger<AbandonedCallImportWorker> logger) : BackgroundService
{
    /// <summary>After the migrations and the first sign-ins; soon enough that a restart leaves no gap.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(20);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
            await scope.ServiceProvider.GetRequiredService<AbandonedCallImport>().RunIfDueAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The server is shutting down. Not a failure.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The abandoned-call import failed. It will be tried again at the next interval.");
        }
    }
}
