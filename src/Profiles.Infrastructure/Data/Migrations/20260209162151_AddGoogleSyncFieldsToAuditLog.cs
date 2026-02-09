using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Profiles.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleSyncFieldsToAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ErrorMessage",
                table: "audit_log",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ResourceId",
                table: "audit_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Role",
                table: "audit_log",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "Success",
                table: "audit_log",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SyncSource",
                table: "audit_log",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UserEmail",
                table: "audit_log",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_ResourceId",
                table: "audit_log",
                column: "ResourceId");

            migrationBuilder.AddForeignKey(
                name: "FK_audit_log_google_resources_ResourceId",
                table: "audit_log",
                column: "ResourceId",
                principalTable: "google_resources",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_audit_log_google_resources_ResourceId",
                table: "audit_log");

            migrationBuilder.DropIndex(
                name: "IX_audit_log_ResourceId",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "ErrorMessage",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "ResourceId",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "Role",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "Success",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "SyncSource",
                table: "audit_log");

            migrationBuilder.DropColumn(
                name: "UserEmail",
                table: "audit_log");
        }
    }
}
