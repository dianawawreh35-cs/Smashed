using CallCenter.Server.Data;
using CallCenter.Server.Features.Pbx;
using CallCenter.Shared;
using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Workers;

/// <summary>
/// Keeps the server subscribed to every agent's extension on the PBX, so the
/// supervisor sees who is offline, free, ringing or in a call (S-61).
/// </summary>
/// <remarks>
/// <para>
/// Every <see cref="Tick"/> it reads the server's own extension and the active
/// agents' extensions, and hands them to a <see cref="PbxSubscriber"/>, which
/// starts, renews and ends subscriptions to match. A new agent, a changed
/// extension or a disabled account is picked up within one tick. A change to
/// the server's extension, its password or the PBX address starts a new
/// subscriber from scratch.
/// </para>
/// <para>
/// Off until the server's extension is entered on the settings screen, as the
/// blacklist and the queue switch are. Like the other jobs it cannot take the
/// server down: a failed run is logged and the timer carries on.
/// </para>
/// </remarks>
public sealed class ExtensionWatchWorker(
    IServiceScopeFactory scopes,
    ExtensionWatch watch,
    TimeProvider clock,
    ILogger<ExtensionWatchWorker> logger) : BackgroundService
{
    /// <summary>After the migrations.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Often enough to renew a one-minute subscription on time and to pick up a
    /// new agent quickly. Most ticks send nothing.
    /// </summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);

    private PbxSubscriber? _subscriber;

    /// <summary>Whether "off until set up" has been logged since the extension was last there.</summary>
    private bool _saidOff;

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

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await RunAsync(stoppingToken);

                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        finally
        {
            _subscriber?.Dispose();
            _subscriber = null;
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var line = await scope.ServiceProvider.GetRequiredService<PbxFeatureLine>().GetAsync(ct);

            if (line is null)
            {
                if (_subscriber is not null)
                {
                    logger.LogInformation("PBX watch: stopped, the server's extension is no longer set up");
                    _subscriber.Dispose();
                    _subscriber = null;
                }
                else if (!_saidOff)
                {
                    // Once, so an empty log is never the only clue (found on
                    // the server's first v0.3.0 start, 26 Sep).
                    logger.LogInformation(
                        "PBX watch: off until the server's extension is entered on the PBX blacklist card in Settings");
                }

                _saidOff = true;
                watch.Failing(ExtensionWatch.Problems.NotConfigured);
                return;
            }

            _saidOff = false;

            if (_subscriber is not null && _subscriber.Line != line)
            {
                logger.LogInformation("PBX watch: the server's extension or the PBX address changed; starting again");
                _subscriber.Dispose();
                _subscriber = null;
            }

            if (_subscriber is null)
            {
                logger.LogInformation("PBX watch: subscribing to the agents' extensions on {Host} as {Extension}",
                    line.Host, line.Extension);
                _subscriber = new PbxSubscriber(line, watch, clock, logger);
            }

            var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();
            var extensions = await db.Users.AsNoTracking()
                .Where(u => u.Role == UserRoles.Agent && u.IsActive && u.Extension != null && u.Extension != "")
                .Select(u => u.Extension!)
                .Distinct()
                .ToListAsync(ct);

            watch.Keep(extensions);
            await _subscriber.Tick(extensions);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PBX watch: this run failed; trying again in {Tick}", Tick);
        }
    }
}
