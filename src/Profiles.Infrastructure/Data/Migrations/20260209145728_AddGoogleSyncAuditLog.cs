using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Profiles.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleSyncAuditLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "google_sync_audit",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ResourceId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserEmail = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Timestamp = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_google_sync_audit", x => x.Id);
                    table.ForeignKey(
                        name: "FK_google_sync_audit_google_resources_ResourceId",
                        column: x => x.ResourceId,
                        principalTable: "google_resources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_google_sync_audit_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_google_sync_audit_Action",
                table: "google_sync_audit",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_google_sync_audit_ResourceId",
                table: "google_sync_audit",
                column: "ResourceId");

            migrationBuilder.CreateIndex(
                name: "IX_google_sync_audit_Timestamp",
                table: "google_sync_audit",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_google_sync_audit_UserId",
                table: "google_sync_audit",
                column: "UserId");

            // Immutability trigger — prevent UPDATE and DELETE on google_sync_audit
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION prevent_google_sync_audit_modification()
                RETURNS TRIGGER AS $$
                BEGIN
                    IF TG_OP = 'UPDATE' THEN
                        RAISE EXCEPTION 'UPDATE operations are not allowed on google_sync_audit table. Audit entries are immutable.';
                    ELSIF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'DELETE operations are not allowed on google_sync_audit table. Audit entries are immutable.';
                    END IF;
                    RETURN NULL;
                END;
                $$ LANGUAGE plpgsql;

                DROP TRIGGER IF EXISTS prevent_google_sync_audit_update ON google_sync_audit;
                CREATE TRIGGER prevent_google_sync_audit_update
                    BEFORE UPDATE ON google_sync_audit
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_google_sync_audit_modification();

                DROP TRIGGER IF EXISTS prevent_google_sync_audit_delete ON google_sync_audit;
                CREATE TRIGGER prevent_google_sync_audit_delete
                    BEFORE DELETE ON google_sync_audit
                    FOR EACH ROW
                    EXECUTE FUNCTION prevent_google_sync_audit_modification();

                COMMENT ON TABLE google_sync_audit IS 'Immutable audit trail of Google resource permission changes. INSERT only - UPDATE and DELETE are blocked by trigger.';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS prevent_google_sync_audit_update ON google_sync_audit;
                DROP TRIGGER IF EXISTS prevent_google_sync_audit_delete ON google_sync_audit;
                DROP FUNCTION IF EXISTS prevent_google_sync_audit_modification();
                """);

            migrationBuilder.DropTable(
                name: "google_sync_audit");
        }
    }
}
