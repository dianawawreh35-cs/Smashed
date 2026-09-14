using Microsoft.EntityFrameworkCore;

namespace CallCenter.Server.Data;

/// <summary>
/// EF Core context for the call centre database.
/// </summary>
/// <remarks>
/// Deliberately empty at this stage. Entities, configurations and the initial
/// migration are added in the next prompt from <c>docs/SCHEMA.md</c>; this type
/// exists now only so the DI wiring, connection string and health of the
/// PostgreSQL connection can be exercised.
/// </remarks>
public class CallCenterDbContext(DbContextOptions<CallCenterDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity configurations are picked up from this assembly once they exist.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CallCenterDbContext).Assembly);
    }
}
