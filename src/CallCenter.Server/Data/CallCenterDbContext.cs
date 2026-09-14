using System.Text.RegularExpressions;
using CallCenter.Server.Data.Entities;
using Microsoft.EntityFrameworkCore;
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
    public DbSet<ClassificationHistory> ClassificationHistory => Set<ClassificationHistory>();

    // Follow-up
    public DbSet<FollowUpTask> FollowUpTasks => Set<FollowUpTask>();

    // Plumbing and audit
    public DbSet<PbxEventRaw> PbxEventsRaw => Set<PbxEventRaw>();
    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();
    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<OutboxSync> OutboxSync => Set<OutboxSync>();

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
