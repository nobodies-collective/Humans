using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NodaTime;

#nullable disable

namespace Humans.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddContainers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "containers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CampSeasonId = table.Column<Guid>(type: "uuid", nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ImageStoragePath = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    ImageContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ImageFileName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<Instant>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_containers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_containers_camp_seasons_CampSeasonId",
                        column: x => x.CampSeasonId,
                        principalTable: "camp_seasons",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_containers_CampSeasonId",
                table: "containers",
                column: "CampSeasonId");

            migrationBuilder.CreateIndex(
                name: "IX_containers_Year",
                table: "containers",
                column: "Year");

            // Data migration: convert ContainerCount/ContainerNotes scalar fields to Container rows
            migrationBuilder.Sql(@"
                INSERT INTO containers (""Id"", ""CampSeasonId"", ""Year"", ""Name"", ""Description"", ""SortOrder"", ""CreatedAt"", ""UpdatedAt"")
                SELECT
                    gen_random_uuid(),
                    cs.""Id"",
                    cs.""Year"",
                    'Container #' || n::text,
                    CASE WHEN n = 1 THEN cs.""ContainerNotes"" ELSE NULL END,
                    n,
                    NOW() AT TIME ZONE 'UTC',
                    NOW() AT TIME ZONE 'UTC'
                FROM camp_seasons cs
                CROSS JOIN generate_series(1, cs.""ContainerCount"") AS gs(n)
                WHERE cs.""ContainerCount"" > 0;
            ");

            migrationBuilder.DropColumn(
                name: "ContainerCount",
                table: "camp_seasons");

            migrationBuilder.DropColumn(
                name: "ContainerNotes",
                table: "camp_seasons");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ContainerCount",
                table: "camp_seasons",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ContainerNotes",
                table: "camp_seasons",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            // Restore scalar data from container rows (best-effort: count per season, first description)
            migrationBuilder.Sql(@"
                UPDATE camp_seasons cs
                SET
                    ""ContainerCount"" = agg.cnt,
                    ""ContainerNotes"" = agg.notes
                FROM (
                    SELECT
                        ""CampSeasonId"",
                        COUNT(*)::int AS cnt,
                        MIN(CASE WHEN ""SortOrder"" = 1 THEN ""Description"" END) AS notes
                    FROM containers
                    WHERE ""CampSeasonId"" IS NOT NULL
                    GROUP BY ""CampSeasonId""
                ) agg
                WHERE cs.""Id"" = agg.""CampSeasonId"";
            ");

            migrationBuilder.DropTable(
                name: "containers");
        }
    }
}
