using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContactSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "contacts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Agent");

            // R-16, F-10 of the 27 Sep review. Nothing recorded where the
            // existing contacts came from, so it is worked out:
            //   - somebody's name on it: typed in by a person (Agent, the default);
            //   - nobody's, made in the few seconds the seed ran: the old
            //     system's customer book (Seed). The seed only runs on an empty
            //     database and loads every contact in one go, about ten seconds;
            //   - nobody's, made at any other time: the POS lookup (Pos), the
            //     only other thing that makes a contact without a user.
            // On the development database on 27 Sep that gave 15,288 Seed, 1 Pos
            // and 121 Agent.
            migrationBuilder.Sql(
                """
                UPDATE contacts SET source = 'Seed'
                 WHERE created_by IS NULL
                   AND created_at < (SELECT min(created_at) + interval '10 minutes'
                                       FROM contacts WHERE created_by IS NULL);
                UPDATE contacts SET source = 'Pos'
                 WHERE created_by IS NULL AND source <> 'Seed';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_contacts_source",
                table: "contacts",
                sql: "source IN ('Seed','Agent','Pos')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_contacts_source",
                table: "contacts");

            migrationBuilder.DropColumn(
                name: "source",
                table: "contacts");
        }
    }
}
