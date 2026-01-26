using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddServerMapStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerMapStats",
                columns: table => new
                {
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalPlayTimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgConcurrentPlayers = table.Column<double>(type: "REAL", nullable: false),
                    PeakConcurrentPlayers = table.Column<int>(type: "INTEGER", nullable: false),
                    Team1Victories = table.Column<int>(type: "INTEGER", nullable: false),
                    Team2Victories = table.Column<int>(type: "INTEGER", nullable: false),
                    Team1Label = table.Column<string>(type: "TEXT", nullable: true),
                    Team2Label = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerMapStats", x => new { x.ServerGuid, x.MapName, x.Year, x.Month });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerMapStats_ServerGuid",
                table: "ServerMapStats",
                column: "ServerGuid");

            migrationBuilder.CreateIndex(
                name: "IX_ServerMapStats_ServerGuid_Year_Month",
                table: "ServerMapStats",
                columns: new[] { "ServerGuid", "Year", "Month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerMapStats");
        }
    }
}
