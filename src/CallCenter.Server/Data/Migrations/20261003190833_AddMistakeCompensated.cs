using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Adds <c>mistakes.compensated</c> (S-65, Dia, 3 Oct 2026): the customer has
    /// been compensated for the mistake (تم التعويض). Every mistake already
    /// recorded starts as not compensated.
    /// </summary>
    public partial class AddMistakeCompensated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "compensated",
                table: "mistakes",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "compensated",
                table: "mistakes");
        }
    }
}
