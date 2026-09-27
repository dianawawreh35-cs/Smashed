using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CallCenter.Server.Data;

/// <summary>
/// Whether the database answers, for <c>/health/ready</c> (M-D04 of the 27 Sep review).
/// </summary>
/// <remarks>
/// <c>/health</c> says only that the process is up, and that is what the
/// container's own health check and the tests use: a restart of the database
/// should not get the API container marked unhealthy. <c>update.sh</c> asks
/// <c>/health/ready</c> instead, so a release that starts but cannot reach its
/// database, or whose migration failed, is not declared live.
/// </remarks>
public sealed class DatabaseHealthCheck(CallCenterDbContext db) : IHealthCheck
{
    public const string Tag = "ready";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            if (!await db.Database.CanConnectAsync(ct))
            {
                return HealthCheckResult.Unhealthy("The database does not answer.");
            }

            // Pending migrations mean the schema is not the one this build expects.
            var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
            return pending.Count == 0
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy($"{pending.Count} database migration(s) not applied.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The database could not be checked.", ex);
        }
    }
}
