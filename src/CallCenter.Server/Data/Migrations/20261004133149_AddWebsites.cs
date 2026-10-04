using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "websites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name_ar = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    name_en = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    login = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    username = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    password_secret = table.Column<string>(type: "text", nullable: true),
                    alerts_with_sound = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    cart_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    username_selector = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    password_selector = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    submit_selector = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_websites", x => x.id);
                    table.CheckConstraint("ck_websites_login", "login IN ('own','shared')");
                });

            // A-88: the POS is the first tab, already there, taking the caller's
            // cart as A-85 did from the Agent App's own settings. Each agent's
            // own login. The supervisor corrects the address if the POS's start
            // page is elsewhere.
            migrationBuilder.Sql("""
                INSERT INTO websites (name_ar, name_en, url, login, cart_url, sort_order)
                VALUES ('نقطة البيع', 'POS', 'https://smashed-ps.com/app', 'own',
                        'https://smashed-ps.com/app/cart/{number}', 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "websites");
        }
    }
}
