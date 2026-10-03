using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionDoNotDisturb : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "do_not_disturb",
                table: "agent_sessions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "do_not_disturb_since",
                table: "agent_sessions",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "do_not_disturb",
                table: "agent_sessions");

            migrationBuilder.DropColumn(
                name: "do_not_disturb_since",
                table: "agent_sessions");
        }
    }
}
