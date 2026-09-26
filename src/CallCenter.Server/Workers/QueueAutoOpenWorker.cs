using CallCenter.Server.Features.Pbx;

namespace CallCenter.Server.Workers;

/// <summary>
/// Opens the call queue every morning at <c>queue.auto_open_time</c> (S-60).
/// </summary>
/// <remarks>
/// <b>It wakes every <see cref="Tick"/> and usually does nothing.</b>
/// <see cref="PbxQueueSwitch.AutoOpenIfDueAsync"/> decides: before the time,
/// once today is dealt with, or with the setting blank, it returns at once. So
/// a new time on the settings screen needs no restart, and the opening is at
/// most half a minute late.
///
/// Its own timer rather than the blacklist's: a blacklist pass can dial for
/// minutes on end, and the opening should not wait behind it.
///
/// <b>It cannot take the server down.</b> As with the other jobs, every run is
/// wrapped: a failure is logged and the timer carries on.
/// </remarks>
public class QueueAutoOpenWorker(
    IServiceScopeFactory scopes,
    ILogger<QueueAutoOpenWorker> logger) : BackgroundService
{
    /// <summary>After the migrations; well inside the hour the opening may still happen in.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(30);

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
            await scope.ServiceProvider.GetRequiredService<PbxQueueSwitch>().AutoOpenIfDueAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The server is shutting down. Not a failure.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The daily queue opening failed. It will be tried again at the next tick.");
        }
    }
}
