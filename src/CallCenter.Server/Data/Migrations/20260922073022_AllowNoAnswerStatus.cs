using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CallCenter.Server.Data.Migrations
{
    /// <summary>
    /// Adds <c>NoAnswer</c> to the statuses a communication may have (A-20, A-21).
    /// </summary>
    /// <remarks>
    /// An outbound call the customer did not pick up. Deliberately <b>not</b>
    /// recorded as <c>Missed</c>: Missed means a customer rang this call centre
    /// and nobody answered, which is the service failure the supervisor's
    /// reports count. Recording a customer who was out as one of those would
    /// have spoiled that figure silently, and it would have been a valid value
    /// in a valid column the whole time.
    ///
    /// Widening a CHECK constraint only, so there is nothing to backfill and
    /// nothing existing changes meaning.
    /// </remarks>
    public partial class AllowNoAnswerStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_communications_status",
                table: "communications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_communications_status",
                table: "communications",
                sql: "status IN ('Ringing','Answered','Missed','Rejected','Blocked','Abandoned','Overflowed','NoAnswer','Failed','Logged')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_communications_status",
                table: "communications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_communications_status",
                table: "communications",
                sql: "status IN ('Ringing','Answered','Missed','Rejected','Blocked','Abandoned','Overflowed','Failed','Logged')");
        }
    }
}
