using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// One extension per agent (SRS 2.3). Internal calls are now told apart by
    /// the other party's number against the list in S-48, not by a second
    /// extension, so <c>internal_extension</c> and its secret go away and
    /// <c>customer_extension</c> becomes simply <c>extension</c>.
    /// </summary>
    /// <remarks>
    /// <b>Written by hand, against what EF scaffolded.</b> EF chose to keep the
    /// internal columns and drop the customer ones, which is the wrong half: the
    /// customer extension is the one agents actually use and the one already
    /// filled in. Keeping EF's version would have thrown away every working
    /// extension number and secret, leaving whatever placeholder sat in the
    /// internal column.
    ///
    /// This still loses the internal extensions and their secrets. That is the
    /// intent — those extensions are no longer part of the design — but it
    /// cannot be undone, so take a backup before applying it to a database whose
    /// contents matter.
    /// </remarks>
    public partial class SingleExtensionPerAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The second extension is gone from the design.
            migrationBuilder.DropColumn(name: "internal_extension", table: "users");
            migrationBuilder.DropColumn(name: "internal_sip_secret", table: "users");

            // The one agents use keeps its values and loses its qualifier.
            migrationBuilder.RenameColumn(
                name: "customer_extension", table: "users", newName: "extension");
            migrationBuilder.RenameColumn(
                name: "customer_sip_secret", table: "users", newName: "sip_secret");

            // Left behind by the pbx.ip -> pbx.host rename, which changed the
            // column names but not this settings row. Nothing reads it.
            migrationBuilder.Sql("DELETE FROM settings WHERE key = 'pbx.ip';");

            // S-48. Seeded for new databases; added here for existing ones so the
            // settings screen starts from a real row rather than an absent one.
            migrationBuilder.Sql(
                "INSERT INTO settings (key, value) VALUES ('reports.internal_numbers', '') " +
                "ON CONFLICT (key) DO NOTHING;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM settings WHERE key = 'reports.internal_numbers';");

            migrationBuilder.RenameColumn(
                name: "extension", table: "users", newName: "customer_extension");
            migrationBuilder.RenameColumn(
                name: "sip_secret", table: "users", newName: "customer_sip_secret");

            // The internal extensions cannot be brought back - their values were
            // dropped. These are empty columns of the right shape.
            migrationBuilder.AddColumn<string>(
                name: "internal_extension", table: "users", type: "text", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "internal_sip_secret", table: "users", type: "text", nullable: true);
        }
    }
}
