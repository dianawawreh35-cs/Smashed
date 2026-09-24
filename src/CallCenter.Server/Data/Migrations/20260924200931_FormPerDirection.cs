using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Gives each form a direction, so inbound and outbound calls have their own
    /// questions (A-21, S-40).
    /// </summary>
    /// <remarks>
    /// Every existing row is inbound: that is what the form meant until now.
    /// The current-form index becomes one-per-direction, and a database that
    /// already has calls gets a first outbound form (type, notes, follow-up)
    /// numbered after whatever exists, so agents can classify an outgoing call
    /// before the supervisor has looked at it. Going down removes the outbound
    /// rows, which is refused if a call was classified against one.
    /// </remarks>
    public partial class FormPerDirection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_form_current",
                table: "form_definitions");

            migrationBuilder.AddColumn<string>(
                name: "direction",
                table: "form_definitions",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "In");

            migrationBuilder.CreateIndex(
                name: "ux_form_current",
                table: "form_definitions",
                column: "direction",
                unique: true,
                filter: "is_current");

            migrationBuilder.Sql($$"""
                INSERT INTO form_definitions (version, definition, direction, is_current)
                SELECT COALESCE(MAX(version), 0) + 1, '{{Data.Seed.SeedData.FormDefinitionOutV1.Replace("'", "''")}}'::jsonb, 'Out', true
                FROM form_definitions
                WHERE NOT EXISTS (SELECT 1 FROM form_definitions WHERE direction = 'Out');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM form_definitions WHERE direction = 'Out';");

            migrationBuilder.DropIndex(
                name: "ux_form_current",
                table: "form_definitions");

            migrationBuilder.DropColumn(
                name: "direction",
                table: "form_definitions");

            migrationBuilder.CreateIndex(
                name: "ux_form_current",
                table: "form_definitions",
                column: "is_current",
                unique: true,
                filter: "is_current");
        }
    }
}
