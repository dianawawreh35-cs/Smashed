using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "menu_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_normalised = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "menu_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name_normalised = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    meal_price = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    is_surcharge = table.Column<bool>(type: "boolean", nullable: false),
                    image = table.Column<byte[]>(type: "bytea", nullable: true),
                    image_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_items", x => x.id);
                    table.CheckConstraint("ck_menu_items_meal_price", "meal_price IS NULL OR meal_price >= 0");
                    table.CheckConstraint("ck_menu_items_price", "price IS NULL OR price >= 0");
                    table.ForeignKey(
                        name: "fk_menu_items_menu_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "menu_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_menu_items_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_menu_items_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_menu_category_name",
                table: "menu_categories",
                column: "name_normalised",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_menu_item_name",
                table: "menu_items",
                column: "name_normalised");

            migrationBuilder.CreateIndex(
                name: "ix_menu_item_order",
                table: "menu_items",
                columns: new[] { "category_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_created_by",
                table: "menu_items",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_updated_by",
                table: "menu_items",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ux_menu_item_name_in_category",
                table: "menu_items",
                columns: new[] { "category_id", "name_normalised" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "menu_items");

            migrationBuilder.DropTable(
                name: "menu_categories");
        }
    }
}
