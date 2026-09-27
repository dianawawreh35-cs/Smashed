using CallCenter.Server.Features.AgentLogs;

namespace CallCenter.Server.Workers;

/// <summary>
/// Deletes the Agent Apps' logs older than <c>AgentLogs:RetentionDays</c>
/// (N-12), once a day.
/// </summary>
/// <remarks>
/// On the same pattern as <see cref="RecordingRetentionWorker"/>: a late first
/// pass, and a failure is logged and tried again tomorrow rather than stopping
/// the job for good.
/// </remarks>
public class AgentLogRetentionWorker(
    AgentLogStore store,
    ILogger<AgentLogRetentionWorker> logger) : BackgroundService
{
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(10);

    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

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

        using var timer = new PeriodicTimer(Interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var deleted = store.DeleteExpired(DateTime.UtcNow);

                if (deleted > 0)
                {
                    logger.LogInformation("Deleted {Count} Agent App log files past their retention", deleted);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The Agent App log retention run failed. It will be tried again at the next interval.");
            }

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
}
