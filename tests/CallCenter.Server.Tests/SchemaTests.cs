using CallCenter.Server.Data;
using CallCenter.Shared;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace CallCenter.Server.Tests;

/// <summary>
/// Verifies the EF Core model against <c>docs/SCHEMA.md</c>, which is the source
/// of truth for the database.
/// </summary>
/// <remarks>
/// These inspect the model, not a live database, so they need no PostgreSQL and
/// run in CI. Applying the migration to a real database is exercised separately
/// by <c>dotnet ef database update</c> during development.
/// </remarks>
public class SchemaTests
{
    /// <summary>
    /// The <b>design-time</b> model. <c>DbContext.Model</c> is read-optimised and
    /// drops metadata only migrations need — check constraints, Postgres
    /// extensions, index methods — which is exactly what these tests assert on.
    /// Built once; nothing here mutates it.
    /// </summary>
    private static readonly Lazy<IModel> DesignTimeModel = new(() =>
    {
        var options = new DbContextOptionsBuilder<CallCenterDbContext>()
            .UseNpgsql("Host=localhost;Database=schema-tests;Username=x;Password=x")
            .Options;

        using var context = new CallCenterDbContext(options);
        return context.GetService<IDesignTimeModel>().Model;
    });

    private static IModel Model => DesignTimeModel.Value;

    /// <summary>Every table in docs/SCHEMA.md, in the order the document introduces them.</summary>
    public static readonly string[] ExpectedTables =
    [
        "branches", "channels", "users", "settings",
        "contacts", "contact_phones",
        "communications", "recordings",
        "classification_types", "form_definitions", "classifications", "classification_history",
        "follow_up_tasks",
        "delivery_areas",
        "pbx_events_raw", "agent_sessions", "audit_log", "outbox_sync",
    ];

    [Fact]
    public void Model_maps_exactly_the_tables_in_the_schema_document()
    {
        var tables = Model.GetEntityTypes()
            .Select(e => e.GetTableName())
            .Where(t => t is not null)
            .Distinct()
            .ToList();

        tables.Should().BeEquivalentTo(ExpectedTables);
    }

    [Fact]
    public void Every_column_is_snake_case()
    {
        var offenders = new List<string>();

        foreach (var entity in Model.GetEntityTypes())
        {
            var table = StoreObjectIdentifier.Create(entity, StoreObjectType.Table);
            if (table is null)
            {
                continue;
            }

            foreach (var property in entity.GetProperties())
            {
                var column = property.GetColumnName(table.Value);
                if (column is null || column.Any(char.IsUpper))
                {
                    offenders.Add($"{table.Value.Name}.{column ?? property.Name}");
                }
            }
        }

        offenders.Should().BeEmpty("the schema document names every column in snake_case");
    }

    [Theory]
    [InlineData("users", "ck_users_role")]
    [InlineData("communications", "ck_communications_kind")]
    [InlineData("communications", "ck_communications_direction")]
    [InlineData("communications", "ck_communications_status")]
    [InlineData("communications", "ck_communications_source")]
    [InlineData("follow_up_tasks", "ck_follow_up_tasks_status")]
    [InlineData("follow_up_tasks", "ck_follow_up_tasks_created_from")]
    [InlineData("pbx_events_raw", "ck_pbx_events_raw_source")]
    public void Check_constraints_from_the_schema_are_present(string table, string constraint)
    {
        var entity = Model.GetEntityTypes().Single(e => e.GetTableName() == table);

        entity.GetCheckConstraints().Select(c => c.Name)
            .Should().Contain(constraint);
    }

    [Theory]
    [InlineData("users", "ck_users_role", new[] { UserRoles.Agent, UserRoles.Supervisor })]
    [InlineData("communications", "ck_communications_kind", new[] { CommunicationKinds.Call, CommunicationKinds.App })]
    [InlineData("communications", "ck_communications_direction", new[] { Directions.In, Directions.Out, Directions.None })]
    [InlineData("follow_up_tasks", "ck_follow_up_tasks_status", new[] { TaskStatuses.Open, TaskStatuses.Done, TaskStatuses.Cancelled })]
    public void Check_constraints_allow_exactly_the_documented_values(
        string table, string constraint, string[] expected)
    {
        var entity = Model.GetEntityTypes().Single(e => e.GetTableName() == table);
        var sql = entity.GetCheckConstraints().Single(c => c.Name == constraint).Sql;

        foreach (var value in expected)
        {
            sql.Should().Contain($"'{value}'");
        }

        // Nothing beyond the documented set.
        sql.Count(c => c == '\'').Should().Be(expected.Length * 2,
            $"{constraint} should allow exactly {expected.Length} values");
    }

    [Fact]
    public void Contact_phone_last9_is_a_stored_generated_column()
    {
        var entity = Model.GetEntityTypes().Single(e => e.GetTableName() == "contact_phones");
        var last9 = entity.GetProperty("Last9");

        last9.GetComputedColumnSql().Should().Be("right(normalised, 9)");
        last9.GetIsStored().Should().BeTrue("the schema declares it STORED");
    }

    [Theory]
    // name, table, unique, filter
    [InlineData("ux_contact_phones_normalised", "contact_phones", true, null)]
    [InlineData("ix_contact_phones_last9", "contact_phones", false, null)]
    [InlineData("ix_comm_started", "communications", false, null)]
    [InlineData("ix_comm_agent_started", "communications", false, null)]
    [InlineData("ix_comm_contact", "communications", false, null)]
    [InlineData("ix_comm_remote", "communications", false, null)]
    [InlineData("ix_comm_status", "communications", false, null)]
    [InlineData("ux_comm_pbx_unique", "communications", true, "pbx_unique_id IS NOT NULL")]
    [InlineData("ux_comm_sip_call", "communications", true, "sip_call_id IS NOT NULL")]
    [InlineData("ux_form_current", "form_definitions", true, "is_current")]
    [InlineData("ix_class_type", "classifications", false, null)]
    [InlineData("ix_class_custom", "classifications", false, null)]
    [InlineData("ix_class_hist_comm", "classification_history", false, null)]
    [InlineData("ix_tasks_open", "follow_up_tasks", false, "status = 'Open'")]
    [InlineData("ix_tasks_contact", "follow_up_tasks", false, null)]
    [InlineData("ix_pbx_events_received", "pbx_events_raw", false, null)]
    [InlineData("ix_sessions_user", "agent_sessions", false, null)]
    [InlineData("ix_audit_entity", "audit_log", false, null)]
    public void Indexes_from_the_schema_are_present(string name, string table, bool unique, string? filter)
    {
        var entity = Model.GetEntityTypes().Single(e => e.GetTableName() == table);
        var index = entity.GetIndexes().SingleOrDefault(i => i.GetDatabaseName() == name);

        index.Should().NotBeNull($"the schema declares index {name} on {table}");
        index!.IsUnique.Should().Be(unique);

        if (filter is not null)
        {
            index.GetFilter().Should().Be(filter);
        }
    }

    [Fact]
    public void Classification_custom_values_index_uses_gin()
    {
        var entity = Model.GetEntityTypes().Single(e => e.GetTableName() == "classifications");
        var index = entity.GetIndexes().Single(i => i.GetDatabaseName() == "ix_class_custom");

        index.GetMethod().Should().Be("gin");
    }

    [Fact]
    public void Guid_primary_keys_default_to_gen_random_uuid()
    {
        // The exceptions are keys the application supplies: a classification is
        // keyed by its communication, and an outbox row by the id the laptop
        // generated.
        string[] applicationSupplied = ["classifications", "outbox_sync"];

        foreach (var entity in Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            var key = entity.FindPrimaryKey();

            if (key is null || key.Properties.Count != 1 || key.Properties[0].ClrType != typeof(Guid))
            {
                continue;
            }

            var property = key.Properties[0];

            if (applicationSupplied.Contains(table))
            {
                property.ValueGenerated.Should().Be(ValueGenerated.Never,
                    $"{table} supplies its own key");
                continue;
            }

            property.GetDefaultValueSql().Should().Be("gen_random_uuid()",
                $"{table}.{property.Name} should be database-generated");
        }
    }

    [Fact]
    public void Pgcrypto_extension_is_declared()
    {
        Model.GetPostgresExtensions().Select(e => e.Name)
            .Should().Contain("pgcrypto", "gen_random_uuid() comes from it");
    }

    [Theory]
    [InlineData("classifications", "CustomValues", "jsonb")]
    [InlineData("form_definitions", "Definition", "jsonb")]
    [InlineData("classification_history", "Before", "jsonb")]
    [InlineData("classification_history", "After", "jsonb")]
    [InlineData("pbx_events_raw", "Payload", "jsonb")]
    [InlineData("audit_log", "Before", "jsonb")]
    [InlineData("audit_log", "After", "jsonb")]
    [InlineData("classifications", "OrderValue", "numeric(10,2)")]
    public void Column_types_match_the_schema(string table, string property, string expected)
    {
        var entity = Model.GetEntityTypes().Single(e => e.GetTableName() == table);

        entity.GetProperty(property).GetColumnType().Should().Be(expected);
    }

    [Fact]
    public void Timestamps_are_timestamptz()
    {
        var offenders = new List<string>();

        foreach (var entity in Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                var clr = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

                // DateTime would map to `timestamp without time zone`, which loses
                // the offset. The schema says timestamptz throughout.
                if (clr == typeof(DateTime))
                {
                    offenders.Add($"{entity.GetTableName()}.{property.Name}");
                }
            }
        }

        offenders.Should().BeEmpty("every timestamp column should be DateTimeOffset, which maps to timestamptz");
    }

    [Fact]
    public void Classification_form_version_references_the_unique_version_column()
    {
        var entity = Model.GetEntityTypes().Single(e => e.GetTableName() == "classifications");
        var fk = entity.GetForeignKeys()
            .Single(f => f.PrincipalEntityType.GetTableName() == "form_definitions");

        fk.PrincipalKey.Properties.Should().ContainSingle()
            .Which.Name.Should().Be("Version",
                "classifications.form_version references form_definitions(version), not its primary key");
    }

    [Theory]
    [InlineData("contact_phones")]      // ON DELETE CASCADE from contacts
    [InlineData("recordings")]          // ON DELETE CASCADE from communications
    [InlineData("classifications")]     // ON DELETE CASCADE from communications
    [InlineData("classification_history")]
    public void Dependent_rows_cascade_with_their_parent(string table)
    {
        var entity = Model.GetEntityTypes().Single(e => e.GetTableName() == table);

        entity.GetForeignKeys().Select(f => f.DeleteBehavior)
            .Should().Contain(DeleteBehavior.Cascade,
                $"the schema declares ON DELETE CASCADE for {table}");
    }
}
