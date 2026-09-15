using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Data;

/// <summary>
/// Brings the database schema up to date at startup.
/// </summary>
/// <remarks>
/// This is what makes step 6 of the deployment runbook true — "on first start
/// the API applies database migrations, creating all tables" — so installing is
/// `docker compose up -d` and nothing else. It is also how a version that adds a
/// column takes effect: load the new image, restart, done.
///
/// Safe to run every start. EF checks <c>__EFMigrationsHistory</c> and applies
/// only what is missing; when nothing is missing it is a single query.
///
/// Fine for one server. If a second API instance is ever added, two could try to
/// migrate at once — at that point this should move behind an advisory lock or
/// out into a deployment step.
/// </remarks>
public static class DatabaseInitialiser
{
    /// <summary>
    /// Applies pending migrations. Set <c>Database:MigrateOnStartup</c> to false
    /// to skip, for a site that would rather run migrations by hand.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitialiser));

        if (!configuration.GetValue("Database:MigrateOnStartup", true))
        {
            logger.LogInformation("Database:MigrateOnStartup is false - skipping migrations");
            return;
        }

        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CallCenterDbContext>();

        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();

        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is up to date");
            return;
        }

        logger.LogInformation("Applying {Count} migration(s): {Migrations}",
            pending.Count, string.Join(", ", pending));

        await db.Database.MigrateAsync(cancellationToken);

        logger.LogInformation("Migrations applied");
    }
}
