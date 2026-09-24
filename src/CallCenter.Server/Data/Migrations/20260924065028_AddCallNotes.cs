using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Adds <c>notes</c> to communications: why a missed, rejected or unanswered call went
    /// the way it did (A-41).
    /// </summary>
    /// <remarks>
    /// Only answered calls are classified. The others had no conversation to
    /// classify, and what is worth keeping about them is the reason, which is a
    /// sentence rather than a form. A nullable column, so nothing to backfill.
    /// </remarks>
    public partial class AddCallNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "communications",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "notes",
                table: "communications");
        }
    }
}
