using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profiles.Infrastructure.Data.Migrations;

/// <summary>
/// Replaces the (TeamId, ResourceType) unique index with (TeamId, GoogleId) to allow
/// multiple Drive resources of the same type per team (e.g. multiple Drive files).
/// </summary>
public partial class ReplaceGoogleResourceUniqueIndex : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Drop the old per-type unique index
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_google_resources_active_team_type";
            """);

        // Create new unique index on (TeamId, GoogleId) where active
        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_google_resources_active_team_googleid"
            ON google_resources("TeamId", "GoogleId")
            WHERE "IsActive" = true AND "TeamId" IS NOT NULL;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_google_resources_active_team_googleid";
            """);

        migrationBuilder.Sql("""
            CREATE UNIQUE INDEX "IX_google_resources_active_team_type"
            ON google_resources("TeamId", "ResourceType")
            WHERE "IsActive" = true AND "TeamId" IS NOT NULL;
            """);
    }
}
