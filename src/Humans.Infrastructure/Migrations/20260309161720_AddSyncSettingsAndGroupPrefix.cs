using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Humans.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncSettingsAndGroupPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleGroupPrefix",
                table: "teams",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sync_service_settings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SyncMode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    UpdatedByUserId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_service_settings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_sync_service_settings_users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.InsertData(
                table: "sync_service_settings",
                columns: new[] { "Id", "ServiceType", "SyncMode", "UpdatedAt", "UpdatedByUserId" },
                values: new object[,]
                {
                    { new Guid("00000000-0000-0000-0002-000000000001"), "GoogleDrive", "None", NodaTime.Instant.FromUnixTimeTicks(17730144000000000L), null },
                    { new Guid("00000000-0000-0000-0002-000000000002"), "GoogleGroups", "None", NodaTime.Instant.FromUnixTimeTicks(17730144000000000L), null },
                    { new Guid("00000000-0000-0000-0002-000000000003"), "Discord", "None", NodaTime.Instant.FromUnixTimeTicks(17730144000000000L), null }
                });

            migrationBuilder.UpdateData(
                table: "teams",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000001"),
                column: "GoogleGroupPrefix",
                value: null);

            migrationBuilder.UpdateData(
                table: "teams",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000002"),
                column: "GoogleGroupPrefix",
                value: null);

            migrationBuilder.UpdateData(
                table: "teams",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000003"),
                column: "GoogleGroupPrefix",
                value: null);

            migrationBuilder.UpdateData(
                table: "teams",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000004"),
                column: "GoogleGroupPrefix",
                value: null);

            migrationBuilder.UpdateData(
                table: "teams",
                keyColumn: "Id",
                keyValue: new Guid("00000000-0000-0000-0001-000000000005"),
                column: "GoogleGroupPrefix",
                value: null);

            migrationBuilder.CreateIndex(
                name: "IX_teams_GoogleGroupPrefix",
                table: "teams",
                column: "GoogleGroupPrefix",
                unique: true,
                filter: "\"GoogleGroupPrefix\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sync_service_settings_ServiceType",
                table: "sync_service_settings",
                column: "ServiceType",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sync_service_settings_UpdatedByUserId",
                table: "sync_service_settings",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_service_settings");

            migrationBuilder.DropIndex(
                name: "IX_teams_GoogleGroupPrefix",
                table: "teams");

            migrationBuilder.DropColumn(
                name: "GoogleGroupPrefix",
                table: "teams");
        }
    }
}
