using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Humans.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVolunteerEventProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "volunteer_event_profiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventSettingsId = table.Column<Guid>(type: "uuid", nullable: false),
                    Skills = table.Column<string>(type: "jsonb", nullable: false),
                    Quirks = table.Column<string>(type: "jsonb", nullable: false),
                    Languages = table.Column<string>(type: "jsonb", nullable: false),
                    DietaryPreference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Allergies = table.Column<string>(type: "jsonb", nullable: false),
                    Intolerances = table.Column<string>(type: "jsonb", nullable: false),
                    MedicalConditions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SuppressScheduleChangeEmails = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_volunteer_event_profiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_volunteer_event_profiles_event_settings_EventSettingsId",
                        column: x => x.EventSettingsId,
                        principalTable: "event_settings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_volunteer_event_profiles_users_UserId",
                        column: x => x.UserId,
                        principalTable: "users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_volunteer_event_profiles_EventSettingsId",
                table: "volunteer_event_profiles",
                column: "EventSettingsId");

            migrationBuilder.CreateIndex(
                name: "IX_volunteer_event_profiles_UserId_EventSettingsId",
                table: "volunteer_event_profiles",
                columns: new[] { "UserId", "EventSettingsId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "volunteer_event_profiles");
        }
    }
}
