using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// A comparison form of the contact's name, for the duplicate-name warning
    /// (A-63).
    /// </summary>
    /// <remarks>
    /// The same Arabic name is written several ways and PostgreSQL compares none
    /// of them as equal — <c>'أحمد' = 'احمد'</c> is false. Matching therefore
    /// compares a folded form rather than the name as typed.
    ///
    /// The backfill below repeats in SQL what
    /// <c>CallCenter.Shared.Text.NameNormalizer</c> does in C#, because existing
    /// rows are not re-saved by the application and would otherwise match
    /// nothing. <b>The C# version is authoritative</b> — it runs on every save
    /// from here on. If the folding rules change, this migration is history and
    /// must not be edited; write a new one.
    /// </remarks>
    public partial class ContactNameNormalised : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name_normalised",
                table: "contacts",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contacts_name_normalised",
                table: "contacts",
                column: "name_normalised");

            // translate() maps the first 8 characters one-for-one and deletes the
            // rest, which have no counterpart in the second argument:
            //   أ إ آ ٱ -> ا      the hamza is routinely left off when typing
            //   ى -> ي            alef maqsura and ya are written either way
            //   ة -> ه            ta marbuta likewise
            //   ؤ -> و, ئ -> ي    hamza carriers
            //   then the nine tashkeel marks and tatweel, deleted outright
            // NULLIF keeps a nameless contact nameless: a bare number saved for
            // flagging (S-45) must not match every other nameless contact.
            migrationBuilder.Sql(
                """
                UPDATE contacts
                SET name_normalised = NULLIF(
                    regexp_replace(
                        btrim(lower(translate(name, 'أإآٱىةؤئًٌٍَُِّْٰـ', 'اااايهوي'))),
                        '\s+', ' ', 'g'),
                    '')
                WHERE name IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_contacts_name_normalised",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "name_normalised",
                table: "contacts");
        }
    }
}
