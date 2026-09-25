using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Gives messages (A-70) their own classification form, stored under
    /// direction <c>None</c> beside the inbound and outbound call forms.
    /// </summary>
    /// <remarks>
    /// No column changes: <c>form_definitions.direction</c> already takes any
    /// ten-character value and the current-form index is already per direction
    /// (FormPerDirection, 24 Sep). A database that already has calls gets a
    /// first Applications form, a copy of the inbound one, numbered after
    /// whatever exists, so an agent can record a WhatsApp order before the
    /// supervisor has looked at the new tab. Going down removes it, which the
    /// database refuses if a message was classified against it.
    /// </remarks>
    public partial class FormForApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($$"""
                INSERT INTO form_definitions (version, definition, direction, is_current)
                SELECT COALESCE(MAX(version), 0) + 1, '{{Data.Seed.SeedData.FormDefinitionAppV1.Replace("'", "''")}}'::jsonb, 'None', true
                FROM form_definitions
                WHERE NOT EXISTS (SELECT 1 FROM form_definitions WHERE direction = 'None');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM form_definitions WHERE direction = 'None';");
        }
    }
}
