using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humans.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillUnderReviewToSubmitted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The UnderReview enum value was removed in commit 32468ea.
            // Status is stored as a string via HasConversion<string>().
            // Any applications stuck with 'UnderReview' should be reset to 'Submitted'
            // so they can proceed through the simplified voting workflow.
            migrationBuilder.Sql(
                """UPDATE applications SET "Status" = 'Submitted' WHERE "Status" = 'UnderReview'""");
            migrationBuilder.Sql(
                """UPDATE application_state_history SET "Status" = 'Submitted' WHERE "Status" = 'UnderReview'""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No rollback — we cannot distinguish which applications were previously UnderReview.
        }
    }
}
