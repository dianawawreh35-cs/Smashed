using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Adds <c>mistakes</c>: the mistakes made by a branch or an agent, as the
    /// supervisor records them on the Mistakes page (S-65).
    /// </summary>
    /// <remarks>
    /// Starts empty, and nothing else changes. <c>ck_mistakes_agent</c> holds the
    /// rule that an agent is named exactly when the mistake is the agent's.
    /// </remarks>
    public partial class AddMistakes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "mistakes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    occurred_on = table.Column<DateOnly>(type: "date", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    responsible = table.Column<string>(type: "text", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    value = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    contact_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_number_raw = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    customer_normalised = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_mistakes", x => x.id);
                    table.CheckConstraint("ck_mistakes_agent", "(responsible = 'Agent') = (agent_id IS NOT NULL)");
                    table.CheckConstraint("ck_mistakes_responsible", "responsible IN ('Branch','Agent')");
                    table.CheckConstraint("ck_mistakes_value", "value IS NULL OR value >= 0");
                    table.ForeignKey(
                        name: "fk_mistakes_branches_branch_id",
                        column: x => x.branch_id,
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mistakes_contacts_contact_id",
                        column: x => x.contact_id,
                        principalTable: "contacts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mistakes_users_agent_id",
                        column: x => x.agent_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mistakes_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_mistakes_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_mistakes_agent_id",
                table: "mistakes",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_mistakes_branch_id",
                table: "mistakes",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_mistakes_contact_id",
                table: "mistakes",
                column: "contact_id");

            migrationBuilder.CreateIndex(
                name: "ix_mistakes_created_by",
                table: "mistakes",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_mistakes_occurred",
                table: "mistakes",
                column: "occurred_on");

            migrationBuilder.CreateIndex(
                name: "ix_mistakes_updated_by",
                table: "mistakes",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "mistakes");
        }
    }
}
