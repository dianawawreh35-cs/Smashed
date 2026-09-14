using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Full-text search over a contact's name and address (A-61, S-02).
    /// </summary>
    /// <remarks>
    /// This lives in its own migration on purpose. It is an <b>expression</b>
    /// index, which cannot be declared on the EF model, so it exists only as the
    /// raw SQL below — and if it were written into InitialSchema, regenerating
    /// that migration would silently delete it. Keeping it separate means
    /// InitialSchema stays disposable and this does not.
    ///
    /// <c>simple</c> rather than a language dictionary: the data is Arabic and
    /// English customer names, where stemming would do more harm than good.
    /// </remarks>
    public partial class AddContactsFullTextIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE INDEX ix_contacts_name ON contacts
                USING gin (to_tsvector('simple', coalesce(name,'') || ' ' || coalesce(address,'')));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_contacts_name;");
        }
    }
}
