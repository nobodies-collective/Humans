using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Humans.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContributionInterestsAndBoardNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BoardNotes",
                table: "profiles",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContributionInterests",
                table: "profiles",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BoardNotes",
                table: "profiles");

            migrationBuilder.DropColumn(
                name: "ContributionInterests",
                table: "profiles");
        }
    }
}
