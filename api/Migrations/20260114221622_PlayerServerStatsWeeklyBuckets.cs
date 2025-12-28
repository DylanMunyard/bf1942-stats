using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class PlayerServerStatsWeeklyBuckets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerLeaderboardEntries");

            // Clear existing data - it's monthly bucketed and will be invalid after column rename
            // The backfill job will repopulate with weekly buckets
            migrationBuilder.Sql("DELETE FROM \"PlayerServerStats\"");

            migrationBuilder.RenameColumn(
                name: "Month",
                table: "PlayerServerStats",
                newName: "Week");

            migrationBuilder.RenameIndex(
                name: "IX_PlayerServerStats_Year_Month",
                table: "PlayerServerStats",
                newName: "IX_PlayerServerStats_Year_Week");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerServerStats_ServerGuid_Year_Week",
                table: "PlayerServerStats",
                columns: new[] { "ServerGuid", "Year", "Week" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerServerStats_ServerGuid_Year_Week",
                table: "PlayerServerStats");

            migrationBuilder.RenameColumn(
                name: "Week",
                table: "PlayerServerStats",
                newName: "Month");

            migrationBuilder.RenameIndex(
                name: "IX_PlayerServerStats_Year_Week",
                table: "PlayerServerStats",
                newName: "IX_PlayerServerStats_Year_Month");

            migrationBuilder.CreateTable(
                name: "ServerLeaderboardEntries",
                columns: table => new
                {
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    Period = table.Column<string>(type: "TEXT", nullable: false),
                    RankingType = table.Column<string>(type: "TEXT", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    TotalDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalKills = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalPlayTimeMinutes = table.Column<double>(type: "REAL", nullable: false),
                    TotalRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalScore = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerLeaderboardEntries", x => new { x.ServerGuid, x.Period, x.RankingType, x.Rank });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerLeaderboardEntries_PlayerName",
                table: "ServerLeaderboardEntries",
                column: "PlayerName");
        }
    }
}
