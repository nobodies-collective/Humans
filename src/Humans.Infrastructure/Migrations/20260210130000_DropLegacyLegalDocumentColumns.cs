using Microsoft.EntityFrameworkCore.Migrations;

#nullable enable

namespace Humans.Infrastructure.Migrations;

/// <inheritdoc />
public partial class DropLegacyLegalDocumentColumns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Drop legacy Type column and its index
        migrationBuilder.DropIndex(
            name: "IX_legal_documents_Type",
            table: "legal_documents");

        migrationBuilder.DropColumn(
            name: "Type",
            table: "legal_documents");

        // Drop legacy GitHubPath column
        migrationBuilder.DropColumn(
            name: "GitHubPath",
            table: "legal_documents");

        // Drop legacy ContentSpanish and ContentEnglish columns
        migrationBuilder.DropColumn(
            name: "ContentSpanish",
            table: "document_versions");

        migrationBuilder.DropColumn(
            name: "ContentEnglish",
            table: "document_versions");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Type",
            table: "legal_documents",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "GitHubPath",
            table: "legal_documents",
            type: "character varying(512)",
            maxLength: 512,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateIndex(
            name: "IX_legal_documents_Type",
            table: "legal_documents",
            column: "Type");

        migrationBuilder.AddColumn<string>(
            name: "ContentSpanish",
            table: "document_versions",
            type: "text",
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "ContentEnglish",
            table: "document_versions",
            type: "text",
            nullable: false,
            defaultValue: "");
    }
}
