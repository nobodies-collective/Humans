using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Profiles.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPreferredEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PreferredEmail",
                table: "users",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<Instant>(
                name: "PreferredEmailVerificationSentAt",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PreferredEmailVerified",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_users_PreferredEmail",
                table: "users",
                column: "PreferredEmail",
                unique: true,
                filter: "\"PreferredEmailVerified\" = true AND \"PreferredEmail\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_PreferredEmail",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PreferredEmail",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PreferredEmailVerificationSentAt",
                table: "users");

            migrationBuilder.DropColumn(
                name: "PreferredEmailVerified",
                table: "users");
        }
    }
}
