using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Writes the break allowance, <c>breaks.daily_limit_minutes</c> = 60 (A-86),
    /// into <c>settings</c> where it is missing.
    /// </summary>
    /// <remarks>
    /// The <c>seed</c> command adds missing settings too, but an update does not
    /// run it, so the Settings page showed the allowance as an empty box: the
    /// server used 60, and the screen did not say so (Dia, 1 Oct 2026). A value
    /// already there, set by a supervisor, is left alone. Down leaves the row:
    /// it may be the supervisor's.
    /// </remarks>
    public partial class SeedBreakAllowance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "INSERT INTO settings (key, value) VALUES ('breaks.daily_limit_minutes', '60') ON CONFLICT (key) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
