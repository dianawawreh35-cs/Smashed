using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// The two agent extensions are not split by call direction, as the initial
    /// schema assumed. Both make and receive calls; they differ by who is on the
    /// other end (SRS 2.3):
    /// <c>inbound_*</c> becomes <c>customer_*</c> — the extension the queue rings
    /// and the one used to call a customer back — and <c>outbound_*</c> becomes
    /// <c>internal_*</c>, for other agents and the four branches.
    /// </summary>
    /// <remarks>
    /// Renames rather than drop-and-add, so any extensions already entered, and
    /// the encrypted SIP secrets with them, survive the change.
    /// </remarks>
    public partial class RenameExtensionsToCustomerAndInternal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "outbound_sip_secret",
                table: "users",
                newName: "internal_sip_secret");

            migrationBuilder.RenameColumn(
                name: "outbound_extension",
                table: "users",
                newName: "internal_extension");

            migrationBuilder.RenameColumn(
                name: "inbound_sip_secret",
                table: "users",
                newName: "customer_sip_secret");

            migrationBuilder.RenameColumn(
                name: "inbound_extension",
                table: "users",
                newName: "customer_extension");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "internal_sip_secret",
                table: "users",
                newName: "outbound_sip_secret");

            migrationBuilder.RenameColumn(
                name: "internal_extension",
                table: "users",
                newName: "outbound_extension");

            migrationBuilder.RenameColumn(
                name: "customer_sip_secret",
                table: "users",
                newName: "inbound_sip_secret");

            migrationBuilder.RenameColumn(
                name: "customer_extension",
                table: "users",
                newName: "inbound_extension");
        }
    }
}
