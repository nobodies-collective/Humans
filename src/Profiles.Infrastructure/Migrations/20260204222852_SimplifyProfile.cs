using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Profiles.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AddressLine1",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "AddressLine2",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "DateOfBirth",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "profiles");

            migrationBuilder.AddColumn<string>(
                name: "PhoneCountryCode",
                table: "profiles",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PhoneCountryCode",
                table: "profiles");

            migrationBuilder.AddColumn<string>(
                name: "AddressLine1",
                table: "profiles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AddressLine2",
                table: "profiles",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<LocalDate>(
                name: "DateOfBirth",
                table: "profiles",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "profiles",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);
        }
    }
}
