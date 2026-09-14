using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pgcrypto", ",,");

            migrationBuilder.CreateTable(
                name: "branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "channels",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_channels", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "classification_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    label_ar = table.Column<string>(type: "text", nullable: false),
                    label_en = table.Column<string>(type: "text", nullable: false),
                    colour = table.Column<string>(type: "text", nullable: true),
                    is_system = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classification_types", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "pbx_events_raw",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    source = table.Column<string>(type: "text", nullable: false),
                    event_name = table.Column<string>(type: "text", nullable: true),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pbx_events_raw", x => x.id);
                    table.CheckConstraint("ck_pbx_events_raw_source", "source IN ('AMI','CDR','SIP')");
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    login = table.Column<string>(type: "text", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: false),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    inbound_extension = table.Column<string>(type: "text", nullable: true),
                    inbound_sip_secret = table.Column<string>(type: "text", nullable: true),
                    outbound_extension = table.Column<string>(type: "text", nullable: true),
                    outbound_sip_secret = table.Column<string>(type: "text", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_role", "role IN ('Agent','Supervisor')");
                });

            migrationBuilder.CreateTable(
                name: "agent_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    laptop_id = table.Column<string>(type: "text", nullable: false),
                    app_version = table.Column<string>(type: "text", nullable: true),
                    logged_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    logged_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    logout_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_agent_sessions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entity = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<string>(type: "text", nullable: true),
                    action = table.Column<string>(type: "text", nullable: false),
                    before = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    after = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    delivery_notes = table.Column<string>(type: "text", nullable: true),
                    is_vip = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_blocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    flag_reason = table.Column<string>(type: "text", nullable: true),
                    flag_changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    flag_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    merged_into_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_contacts_contacts_merged_into_id",
                        column: x => x.merged_into_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contacts_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contacts_users_flag_changed_by",
                        column: x => x.flag_changed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contacts_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "form_definitions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    version = table.Column<int>(type: "integer", nullable: false),
                    definition = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    is_current = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_form_definitions", x => x.id);
                    table.UniqueConstraint("ak_form_definitions_version", x => x.version);
                    table.ForeignKey(
                        name: "fk_form_definitions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "outbox_sync",
                columns: table => new
                {
                    client_op_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_sync", x => x.client_op_id);
                    table.ForeignKey(
                        name: "fk_outbox_sync_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.key);
                    table.ForeignKey(
                        name: "fk_settings_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "communications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    kind = table.Column<string>(type: "text", nullable: false),
                    channel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    remote_number_raw = table.Column<string>(type: "text", nullable: true),
                    remote_normalised = table.Column<string>(type: "text", nullable: true),
                    remote_name = table.Column<string>(type: "text", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    duration_sec = table.Column<int>(type: "integer", nullable: true),
                    wait_sec = table.Column<int>(type: "integer", nullable: true),
                    queue_name = table.Column<string>(type: "text", nullable: true),
                    extension = table.Column<string>(type: "text", nullable: true),
                    sip_call_id = table.Column<string>(type: "text", nullable: true),
                    pbx_unique_id = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    laptop_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_communications", x => x.id);
                    table.CheckConstraint("ck_communications_direction", "direction IN ('In','Out','None')");
                    table.CheckConstraint("ck_communications_kind", "kind IN ('Call','App')");
                    table.CheckConstraint("ck_communications_source", "source IN ('AgentApp','AMI','CDR','Manual')");
                    table.CheckConstraint("ck_communications_status", "status IN ('Ringing','Answered','Missed','Rejected','Blocked','Abandoned','Overflowed','Failed','Logged')");
                    table.ForeignKey(
                        name: "fk_communications_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_communications_channels_channel_id",
                        column: x => x.channel_id,
                        principalTable: "channels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_communications_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_communications_users_agent_id",
                        column: x => x.agent_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contact_phones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raw = table.Column<string>(type: "text", nullable: false),
                    normalised = table.Column<string>(type: "text", nullable: false),
                    last9 = table.Column<string>(type: "text", nullable: false, computedColumnSql: "right(normalised, 9)", stored: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_phones", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_phones_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "classification_history",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    communication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    before = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    after = table.Column<JsonDocument>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classification_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_classification_history_communications_communication_id",
                        column: x => x.communication_id,
                        principalTable: "communications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_classification_history_users_changed_by",
                        column: x => x.changed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "classifications",
                columns: table => new
                {
                    communication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_value = table.Column<decimal>(type: "numeric(10,2)", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    follow_up = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    resolved = table.Column<bool>(type: "boolean", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    form_version = table.Column<int>(type: "integer", nullable: false),
                    custom_values = table.Column<JsonDocument>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    classified_by = table.Column<Guid>(type: "uuid", nullable: false),
                    classified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_classifications", x => x.communication_id);
                    table.ForeignKey(
                        name: "fk_classifications_classification_types_type_id",
                        column: x => x.type_id,
                        principalTable: "classification_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_classifications_communications_communication_id",
                        column: x => x.communication_id,
                        principalTable: "communications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_classifications_form_definitions_form_version",
                        column: x => x.form_version,
                        principalTable: "form_definitions",
                        principalColumn: "version",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_classifications_users_classified_by",
                        column: x => x.classified_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_classifications_users_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_classifications_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "follow_up_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    communication_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "text", nullable: false),
                    assigned_to = table.Column<Guid>(type: "uuid", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "Open"),
                    created_from = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_by_communication_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follow_up_tasks", x => x.id);
                    table.CheckConstraint("ck_follow_up_tasks_created_from", "created_from IN ('Complaint','Abandoned','Missed','Manual')");
                    table.CheckConstraint("ck_follow_up_tasks_status", "status IN ('Open','Done','Cancelled')");
                    table.ForeignKey(
                        name: "fk_follow_up_tasks_communications_closed_by_communication_id",
                        column: x => x.closed_by_communication_id,
                        principalTable: "communications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_follow_up_tasks_communications_communication_id",
                        column: x => x.communication_id,
                        principalTable: "communications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_follow_up_tasks_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_follow_up_tasks_users_assigned_to",
                        column: x => x.assigned_to,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_follow_up_tasks_users_closed_by",
                        column: x => x.closed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_follow_up_tasks_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "recordings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    communication_id = table.Column<Guid>(type: "uuid", nullable: false),
                    path = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    duration_sec = table.Column<int>(type: "integer", nullable: true),
                    format = table.Column<string>(type: "text", nullable: false, defaultValue: "wav"),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_recordings", x => x.id);
                    table.ForeignKey(
                        name: "fk_recordings_communications_communication_id",
                        column: x => x.communication_id,
                        principalTable: "communications",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sessions_user",
                table: "agent_sessions",
                columns: new[] { "user_id", "logged_in_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_entity",
                table: "audit_log",
                columns: new[] { "entity", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_user_id",
                table: "audit_log",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_branches_name",
                table: "branches",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_channels_name",
                table: "channels",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_class_hist_comm",
                table: "classification_history",
                column: "communication_id");

            migrationBuilder.CreateIndex(
                name: "ix_classification_history_changed_by",
                table: "classification_history",
                column: "changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_classification_types_name",
                table: "classification_types",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_class_custom",
                table: "classifications",
                column: "custom_values")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_class_type",
                table: "classifications",
                column: "type_id");

            migrationBuilder.CreateIndex(
                name: "ix_classifications_classified_by",
                table: "classifications",
                column: "classified_by");

            migrationBuilder.CreateIndex(
                name: "ix_classifications_form_version",
                table: "classifications",
                column: "form_version");

            migrationBuilder.CreateIndex(
                name: "ix_classifications_resolved_by",
                table: "classifications",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "ix_classifications_updated_by",
                table: "classifications",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_comm_agent_started",
                table: "communications",
                columns: new[] { "agent_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_comm_contact",
                table: "communications",
                columns: new[] { "contact_id", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_comm_remote",
                table: "communications",
                column: "remote_normalised");

            migrationBuilder.CreateIndex(
                name: "ix_comm_started",
                table: "communications",
                column: "started_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_comm_status",
                table: "communications",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_communications_branch_id",
                table: "communications",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_communications_channel_id",
                table: "communications",
                column: "channel_id");

            migrationBuilder.CreateIndex(
                name: "ux_comm_pbx_unique",
                table: "communications",
                column: "pbx_unique_id",
                unique: true,
                filter: "pbx_unique_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_comm_sip_call",
                table: "communications",
                columns: new[] { "sip_call_id", "extension" },
                unique: true,
                filter: "sip_call_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contact_phones_contact_id",
                table: "contact_phones",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_contact_phones_last9",
                table: "contact_phones",
                column: "last9");

            migrationBuilder.CreateIndex(
                name: "ux_contact_phones_normalised",
                table: "contact_phones",
                column: "normalised",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_created_by",
                table: "contacts",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_flag_changed_by",
                table: "contacts",
                column: "flag_changed_by");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_merged_into_id",
                table: "contacts",
                column: "merged_into_id");

            migrationBuilder.CreateIndex(
                name: "ix_contacts_updated_by",
                table: "contacts",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_assigned_to",
                table: "follow_up_tasks",
                column: "assigned_to");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_closed_by",
                table: "follow_up_tasks",
                column: "closed_by");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_closed_by_communication_id",
                table: "follow_up_tasks",
                column: "closed_by_communication_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_communication_id",
                table: "follow_up_tasks",
                column: "communication_id");

            migrationBuilder.CreateIndex(
                name: "ix_follow_up_tasks_created_by",
                table: "follow_up_tasks",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_contact",
                table: "follow_up_tasks",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_tasks_open",
                table: "follow_up_tasks",
                columns: new[] { "status", "due_at" },
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_form_definitions_created_by",
                table: "form_definitions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_form_definitions_version",
                table: "form_definitions",
                column: "version",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_form_current",
                table: "form_definitions",
                column: "is_current",
                unique: true,
                filter: "is_current");

            migrationBuilder.CreateIndex(
                name: "ix_outbox_sync_user_id",
                table: "outbox_sync",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_pbx_events_received",
                table: "pbx_events_raw",
                column: "received_at");

            migrationBuilder.CreateIndex(
                name: "ix_recordings_communication_id",
                table: "recordings",
                column: "communication_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_settings_updated_by",
                table: "settings",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_users_login",
                table: "users",
                column: "login",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_sessions");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.DropTable(
                name: "classification_history");

            migrationBuilder.DropTable(
                name: "classifications");

            migrationBuilder.DropTable(
                name: "contact_phones");

            migrationBuilder.DropTable(
                name: "follow_up_tasks");

            migrationBuilder.DropTable(
                name: "outbox_sync");

            migrationBuilder.DropTable(
                name: "pbx_events_raw");

            migrationBuilder.DropTable(
                name: "recordings");

            migrationBuilder.DropTable(
                name: "settings");

            migrationBuilder.DropTable(
                name: "classification_types");

            migrationBuilder.DropTable(
                name: "form_definitions");

            migrationBuilder.DropTable(
                name: "communications");

            migrationBuilder.DropTable(
                name: "branches");

            migrationBuilder.DropTable(
                name: "channels");

            migrationBuilder.DropTable(
                name: "contacts");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
