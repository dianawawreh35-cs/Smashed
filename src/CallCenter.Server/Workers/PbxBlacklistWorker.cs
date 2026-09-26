using CallCenter.Server.Features.Pbx;

namespace CallCenter.Server.Workers;

/// <summary>
/// Brings the PBX's blacklist into line with the Blocked flags (S-46), every
/// <see cref="Tick"/>.
/// </summary>
/// <remarks>
/// <b>It wakes often and usually does nothing.</b> Each run compares the
/// blocked numbers with what the PBX has been told, which is two small queries,
/// and dials only when they differ. So a supervisor who blocks a caller sees
/// the PBX follow within the tick plus the length of the call, with no signal
/// from the flag screen to here, and a server with no extension set up (the
/// test host included) never dials.
///
/// <b>It cannot take the server down.</b> As with the other jobs, every run is
/// wrapped: a failure is logged and the timer carries on. A call the PBX
/// refuses is not a failure here: the sync records why, and the settings screen
/// shows it.
/// </remarks>
public class PbxBlacklistWorker(
    IServiceScopeFactory scopes,
    ILogger<PbxBlacklistWorker> logger) : BackgroundService
{
    /// <summary>After the migrations; soon enough that a restart leaves no gap.</summary>
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
            await scope.ServiceProvider.GetRequiredService<PbxBlacklistSync>().RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The server is shutting down. Not a failure.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The PBX blacklist sync failed. It will be tried again at the next tick.");
        }
    }
}
