using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humans.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EndOrphanedLeadRoleAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Soft-end any existing Lead governance RoleAssignments.
            // The standalone "Lead" RoleAssignment is being removed — Lead status
            // is derived from TeamMemberRole.Lead on user-created teams instead.
            // Preserves audit trail by setting ValidTo rather than deleting.
            migrationBuilder.Sql("""
                UPDATE role_assignments SET "ValidTo" = now()
                WHERE "RoleName" = 'Lead' AND "ValidTo" IS NULL
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Re-opening ended Lead assignments is not safe — we don't know which
            // were ended by this migration vs. ended manually before it ran.
            // No-op; restore manually if needed.
        }
    }
}
