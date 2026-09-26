using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Adds <c>abandoned_call_id</c> to communications: on an Agent App ring
    /// that was not taken, the abandoned call it was a ring of (S-55).
    /// </summary>
    /// <remarks>
    /// The PBX queue rings an agent again and again while a caller waits, and
    /// the Agent App records every ring. On 22 Sep one caller who waited 1:55
    /// and gave up left six Missed and Rejected rows. Pointing those rows at the
    /// imported abandoned call lets the reports count that customer once.
    /// Nullable, so nothing to backfill: the import links the rings itself.
    /// </remarks>
    public partial class LinkRingsToAbandonedCalls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "abandoned_call_id",
                table: "communications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_comm_abandoned_call",
                table: "communications",
                column: "abandoned_call_id",
                filter: "abandoned_call_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_communications_communications_abandoned_call_id",
                table: "communications",
                column: "abandoned_call_id",
                principalTable: "communications",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_communications_communications_abandoned_call_id",
                table: "communications");

            migrationBuilder.DropIndex(
                name: "ix_comm_abandoned_call",
                table: "communications");

            migrationBuilder.DropColumn(
                name: "abandoned_call_id",
                table: "communications");
        }
    }
}
