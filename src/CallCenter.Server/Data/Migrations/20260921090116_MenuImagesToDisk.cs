using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Moves the menu photographs out of the database and onto disk (A-66, S-59).
    /// </summary>
    /// <remarks>
    /// The bytes dropped here are not lost: they ship as embedded resources in
    /// this assembly, and re-running <c>seed</c> writes them to the images
    /// folder and records the file names. <c>Down</c> restores the columns but
    /// not the bytes, for the same reason.
    ///
    /// Why the move: rsync is incremental and <c>pg_dump</c> is not. Menu
    /// photographs never change, so on disk the nightly backup copies them once
    /// rather than re-dumping 1.2 MB every night for ever.
    /// </remarks>
    public partial class MenuImagesToDisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image",
                table: "menu_items");

            migrationBuilder.DropColumn(
                name: "image_content_type",
                table: "menu_items");

            migrationBuilder.AddColumn<string>(
                name: "image_file_name",
                table: "menu_items",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_file_name",
                table: "menu_items");

            migrationBuilder.AddColumn<byte[]>(
                name: "image",
                table: "menu_items",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "image_content_type",
                table: "menu_items",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }
    }
}
