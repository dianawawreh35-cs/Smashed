using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Adds <c>agent_breaks</c>: each break an agent takes in the Agent App,
    /// Break in to Break out (A-86), for the break monitor (S-66) and the break
    /// report (R-22).
    /// </summary>
    /// <remarks>
    /// Starts empty, and nothing else changes. The id is made on the laptop, so a
    /// break resent from the offline queue is the same row. A break whose sign-in
    /// row is removed keeps its times (<c>session_id</c> set null).
    /// </remarks>
    public partial class AddAgentBreaks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "agent_breaks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ended_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agent_breaks", x => x.id);
                    table.CheckConstraint("ck_agent_breaks_end", "(ended_at IS NULL) = (ended_by IS NULL) AND (ended_at IS NULL OR ended_at >= started_at)");
                    table.CheckConstraint("ck_agent_breaks_ended_by", "ended_by IN ('BreakOut','SignedOut','SessionEnded','NotHeard')");
                    table.ForeignKey(
                        name: "fk_agent_breaks_agent_sessions_session_id",
                        column: x => x.session_id,
                        principalTable: "agent_sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_agent_breaks_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_agent_breaks_session_id",
                table: "agent_breaks",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "ix_agent_breaks_started",
                table: "agent_breaks",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "ix_agent_breaks_user",
                table: "agent_breaks",
                columns: new[] { "user_id", "started_at" },
                descending: new[] { false, true });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "agent_breaks");
        }
    }
}
