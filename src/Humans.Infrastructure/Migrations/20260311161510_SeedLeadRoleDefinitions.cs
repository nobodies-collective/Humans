using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humans.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SeedLeadRoleDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO team_role_definitions (""Id"", ""TeamId"", ""Name"", ""Description"", ""SlotCount"", ""Priorities"", ""SortOrder"", ""CreatedAt"", ""UpdatedAt"")
                SELECT gen_random_uuid(), t.""Id"", 'Lead', 'Team leadership role', 1, 'Critical', 0,
                       NOW() AT TIME ZONE 'UTC', NOW() AT TIME ZONE 'UTC'
                FROM teams t
                WHERE t.""SystemTeamType"" = 'None'
                AND NOT EXISTS (
                    SELECT 1 FROM team_role_definitions d WHERE d.""TeamId"" = t.""Id"" AND lower(d.""Name"") = 'lead'
                )
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
