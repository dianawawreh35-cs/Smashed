using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProtectClassificationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_classification_history_communications_communication_id",
                table: "classification_history");

            migrationBuilder.AddForeignKey(
                name: "fk_classification_history_communications_communication_id",
                table: "classification_history",
                column: "communication_id",
                principalTable: "communications",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_classification_history_communications_communication_id",
                table: "classification_history");

            migrationBuilder.AddForeignKey(
                name: "fk_classification_history_communications_communication_id",
                table: "classification_history",
                column: "communication_id",
                principalTable: "communications",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
