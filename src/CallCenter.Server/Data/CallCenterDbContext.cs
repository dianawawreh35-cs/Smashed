using System.Text.RegularExpressions;
using CallCenter.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.EntityFrameworkCore.Metadata;

namespace CallCenter.Server.Data;

/// <summary>
/// EF Core context for the call centre database.
/// </summary>
/// <remarks>
/// The model is defined by the <c>IEntityTypeConfiguration</c> classes in
/// <c>Data/Configurations</c> and must match <c>docs/SCHEMA.md</c> exactly —
/// <c>SchemaTests</c> fails otherwise.
/// </remarks>
public partial class CallCenterDbContext(DbContextOptions<CallCenterDbContext> options) : DbContext(options)
{
    // Organisation
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Channel> Channels => Set<Channel>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Setting> Settings => Set<Setting>();

    // Customers
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<ContactPhone> ContactPhones => Set<ContactPhone>();

    // Communications
    public DbSet<Communication> Communications => Set<Communication>();
    public DbSet<Recording> Recordings => Set<Recording>();

    // Classification
    public DbSet<ClassificationType> ClassificationTypes => Set<ClassificationType>();
    public DbSet<FormDefinition> FormDefinitions => Set<FormDefinition>();
    public DbSet<Classification> Classifications => Set<Classification>();

    /// <summary>Where the restaurant delivers, which branch covers it, and the price (A-65, S-58).</summary>
    public DbSet<DeliveryArea> DeliveryAreas => Set<DeliveryArea>();

    /// <summary>The menu, grouped as the printed one is (A-66, S-59).</summary>
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();

    /// <summary>What is on the menu, with its price and picture (A-66, S-59).</summary>
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<ClassificationHistory> ClassificationHistory => Set<ClassificationHistory>();

    // Follow-up
    public DbSet<FollowUpTask> FollowUpTasks => Set<FollowUpTask>();

    // Plumbing and audit
    public DbSet<PbxEventRaw> PbxEventsRaw => Set<PbxEventRaw>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<OutboxSync> OutboxSync => Set<OutboxSync>();

    /// <summary>
    /// Every <see cref="DateTimeOffset"/> is converted to UTC on the way to the
    /// database.
    /// </summary>
    /// <remarks>
    /// Npgsql refuses to write a <c>DateTimeOffset</c> with a non-zero offset to
    /// <c>timestamp with time zone</c>: only UTC is accepted. Everything written
    /// by the server used <c>UtcNow</c> and so never hit it. The Agent App
    /// stamps a call with <c>DateTimeOffset.Now</c> — correctly, since it is
    /// describing a moment in the agent's day — and that arrives carrying
    /// +03:00, which failed every insert with a 500.
    ///
    /// Fixed here rather than in the one service that hit it, because the next
    /// timestamp to arrive from a client would fail the same way and the fix
    /// would have to be remembered again. Converting loses nothing: the instant
    /// is identical, and the offset was never stored by this column type in the
    /// first place.
    /// </remarks>
    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        base.ConfigureConventions(builder);

        builder.Properties<DateTimeOffset>()
            .HaveConversion<UtcDateTimeOffsetConverter>();

        builder.Properties<DateTimeOffset?>()
            .HaveConversion<UtcDateTimeOffsetConverter>();
    }

    /// <summary>
    /// Normalises to UTC on write and leaves reads alone — what comes back from
    /// <c>timestamptz</c> is already UTC.
    /// </summary>
    private sealed class UtcDateTimeOffsetConverter()
        : ValueConverter<DateTimeOffset, DateTimeOffset>(
            write => write.ToUniversalTime(),
            read => read);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // gen_random_uuid() for the Guid primary keys.
        modelBuilder.HasPostgresExtension("pgcrypto");

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CallCenterDbContext).Assembly);

        ApplyNamingConventions(modelBuilder);
        ApplyGuidKeyDefaults(modelBuilder);
    }

    /// <summary>
    /// Columns, keys, indexes and constraints are <c>snake_case</c> in the schema
    /// document; C# is PascalCase. Rather than spelling out every column name in
    /// the configurations, translate here. Anything a configuration named
    /// explicitly is left alone.
    /// </summary>
    private static void ApplyNamingConventions(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(ToSnakeCase(key.GetName()!));
            }

            foreach (var fk in entity.GetForeignKeys())
            {
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName(ToSnakeCase(index.GetDatabaseName()!));
            }
        }
    }

    /// <summary>
    /// Every <c>uuid</c> primary key defaults to <c>gen_random_uuid()</c>, matching
    /// the schema. Keys the application supplies itself — a classification's
    /// communication id, an outbox operation id — opt out with
    /// <c>ValueGeneratedNever()</c> in their configuration and are skipped here.
    /// </summary>
    private static void ApplyGuidKeyDefaults(ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey();
            if (key is null || key.Properties.Count != 1)
            {
                continue;
            }

            var property = key.Properties[0];
            if (property.ClrType == typeof(Guid)
                && property.ValueGenerated == ValueGenerated.OnAdd
                && property.GetDefaultValueSql() is null)
            {
                property.SetDefaultValueSql("gen_random_uuid()");
            }
        }
    }

    /// <summary>
    /// <c>ContactPhone</c> → <c>contact_phone</c>, <c>IsVip</c> → <c>is_vip</c>,
    /// <c>Last9</c> → <c>last9</c>.
    /// </summary>
    internal static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        // The pattern matches the empty position at a word boundary — between a
        // lower-case letter or digit and an upper-case one, and between an
        // acronym and the word that follows it — so the replacement is just the
        // separator.
        return SnakeCaseBoundary().Replace(name, "_").ToLowerInvariant();
    }

    [GeneratedRegex(@"(?<=[a-z0-9])(?=[A-Z])|(?<=[A-Z])(?=[A-Z][a-z])", RegexOptions.CultureInvariant)]
    private static partial Regex SnakeCaseBoundary();
}
