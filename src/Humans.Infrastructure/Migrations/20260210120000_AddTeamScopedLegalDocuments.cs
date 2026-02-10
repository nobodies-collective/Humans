using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humans.Infrastructure.Migrations
{
    /// <summary>
    /// Adds team-scoped legal documents: TeamId FK, per-document grace period,
    /// folder-based GitHub sync, and multi-language content jsonb column.
    /// Existing documents are assigned to the Volunteers team.
    /// </summary>
    public partial class AddTeamScopedLegalDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add new columns to legal_documents
            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "legal_documents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GracePeriodDays",
                table: "legal_documents",
                type: "integer",
                nullable: false,
                defaultValue: 7);

            migrationBuilder.AddColumn<string>(
                name: "GitHubFolderPath",
                table: "legal_documents",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            // 2. Assign all existing docs to Volunteers team
            migrationBuilder.Sql(
                "UPDATE legal_documents SET \"TeamId\" = '00000000-0000-0000-0001-000000000001'");

            // 3. Extract folder from GitHubPath (e.g. "privacy/privacy-es.md" → "privacy/")
            migrationBuilder.Sql(
                "UPDATE legal_documents SET \"GitHubFolderPath\" = regexp_replace(\"GitHubPath\", '/[^/]+$', '/') WHERE \"GitHubPath\" IS NOT NULL AND \"GitHubPath\" != ''");

            // 4. Make TeamId NOT NULL now that all rows have a value
            migrationBuilder.AlterColumn<Guid>(
                name: "TeamId",
                table: "legal_documents",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            // 5. Add FK and indexes
            migrationBuilder.CreateIndex(
                name: "IX_legal_documents_TeamId_IsActive",
                table: "legal_documents",
                columns: new[] { "TeamId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_legal_documents_teams_TeamId",
                table: "legal_documents",
                column: "TeamId",
                principalTable: "teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 6. Add content jsonb to document_versions
            migrationBuilder.AddColumn<Dictionary<string, string>>(
                name: "Content",
                table: "document_versions",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'{}'::jsonb");

            // 7. Populate content from existing columns
            migrationBuilder.Sql(
                "UPDATE document_versions SET \"Content\" = jsonb_build_object('es', \"ContentSpanish\", 'en', \"ContentEnglish\")");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_legal_documents_teams_TeamId",
                table: "legal_documents");

            migrationBuilder.DropIndex(
                name: "IX_legal_documents_TeamId_IsActive",
                table: "legal_documents");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "legal_documents");

            migrationBuilder.DropColumn(
                name: "GracePeriodDays",
                table: "legal_documents");

            migrationBuilder.DropColumn(
                name: "GitHubFolderPath",
                table: "legal_documents");

            migrationBuilder.DropColumn(
                name: "Content",
                table: "document_versions");
        }
    }
}
