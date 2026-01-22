using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateModelSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapHourlyPatterns");

            migrationBuilder.CreateTable(
                name: "MapServerHourlyPatterns",
                columns: table => new
                {
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    Game = table.Column<string>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    HourOfDay = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgPlayers = table.Column<double>(type: "REAL", nullable: false),
                    TimesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    DataPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapServerHourlyPatterns", x => new { x.ServerGuid, x.MapName, x.Game, x.DayOfWeek, x.HourOfDay });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapServerHourlyPatterns_MapName_Game",
                table: "MapServerHourlyPatterns",
                columns: new[] { "MapName", "Game" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapServerHourlyPatterns");

            migrationBuilder.CreateTable(
                name: "MapHourlyPatterns",
                columns: table => new
                {
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    Game = table.Column<string>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    HourOfDay = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgPlayers = table.Column<double>(type: "REAL", nullable: false),
                    DataPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    TimesPlayed = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapHourlyPatterns", x => new { x.MapName, x.Game, x.DayOfWeek, x.HourOfDay });
                });

            migrationBuilder.CreateIndex(
                name: "IX_MapHourlyPatterns_MapName_Game",
                table: "MapHourlyPatterns",
                columns: new[] { "MapName", "Game" });
        }
    }
}
