using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Adds <c>pbx_blacklist</c>: the numbers the server has put on the PBX's
    /// own blacklist by dialling <c>*30</c>, or is still trying to add or remove
    /// (S-46).
    /// </summary>
    /// <remarks>
    /// Starts empty. The first run after the upgrade finds every number that is
    /// already blocked missing from it and adds them one call at a time.
    /// </remarks>
    public partial class AddPbxBlacklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pbx_blacklist",
                columns: table => new
                {
                    number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    on_pbx = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    failed_attempts = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pbx_blacklist", x => x.number);
                    table.CheckConstraint("ck_pbx_blacklist_number", "number ~ '^[0-9]+$'");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pbx_blacklist");
        }
    }
}
