using CallCenter.Server.Features.Communications;

namespace CallCenter.Server.Workers;

/// <summary>
/// Runs the recording retention pass (A-33) once a day.
/// </summary>
/// <remarks>
/// <b>Nightly is enough.</b> Retention is a 90-day rule; whether a recording
/// expires at midnight or at noon is not a distinction anybody can act on, and
/// a job that walks the whole table every few minutes would cost more than it
/// is worth.
///
/// <b>The first pass is delayed.</b> A server that has just started is applying
/// migrations, seeding and answering the first sign-ins of the shift; the
/// retention job has no reason to compete with any of that.
///
/// <b>It cannot take the server down.</b> Every run is wrapped: a failure is
/// logged and the timer carries on, because a background job that throws out of
/// <c>ExecuteAsync</c> stops for good and nobody notices until the disk fills.
/// The database is reached through a scope of its own, since the context is
/// scoped and this service is not.
/// </remarks>
public class RecordingRetentionWorker(
    IServiceScopeFactory scopes,
    ILogger<RecordingRetentionWorker> logger) : BackgroundService
{
    /// <summary>How long after startup the first pass runs.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    /// <summary>How often it runs after that.</summary>
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
            var retention = scope.ServiceProvider.GetRequiredService<RecordingRetention>();

            await retention.RunOnceAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The server is shutting down. Not a failure.
        }
        catch (Exception ex)
        {
            // A-33: never takes the server down. Tomorrow's run tries again.
            logger.LogError(ex, "The recording retention run failed. It will be tried again at the next interval.");
        }
    }
}
