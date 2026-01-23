using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class MonthlyAggregationSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerStatsLifetimes");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PlayerServerStats",
                table: "PlayerServerStats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PlayerMapStats",
                table: "PlayerMapStats");

            migrationBuilder.DropColumn(
                name: "HighestScoreMapName",
                table: "PlayerServerStats");

            migrationBuilder.DropColumn(
                name: "HighestScoreRoundId",
                table: "PlayerServerStats");

            migrationBuilder.DropColumn(
                name: "HighestScoreTime",
                table: "PlayerServerStats");

            migrationBuilder.RenameColumn(
                name: "HighestScore",
                table: "PlayerServerStats",
                newName: "Month");

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "PlayerServerStats",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Year",
                table: "PlayerMapStats",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Month",
                table: "PlayerMapStats",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PlayerServerStats",
                table: "PlayerServerStats",
                columns: new[] { "PlayerName", "ServerGuid", "Year", "Month" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_PlayerMapStats",
                table: "PlayerMapStats",
                columns: new[] { "PlayerName", "MapName", "ServerGuid", "Year", "Month" });

            migrationBuilder.CreateTable(
                name: "PlayerStatsMonthly",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    Month = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalKills = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalScore = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalPlayTimeMinutes = table.Column<double>(type: "REAL", nullable: false),
                    AvgScorePerRound = table.Column<double>(type: "REAL", nullable: false),
                    KdRatio = table.Column<double>(type: "REAL", nullable: false),
                    KillRate = table.Column<double>(type: "REAL", nullable: false),
                    FirstRoundTime = table.Column<string>(type: "TEXT", nullable: false),
                    LastRoundTime = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerStatsMonthly", x => new { x.PlayerName, x.Year, x.Month });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerServerStats_Year_Month",
                table: "PlayerServerStats",
                columns: new[] { "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMapStats_Year_Month",
                table: "PlayerMapStats",
                columns: new[] { "Year", "Month" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerStatsMonthly_Year_Month",
                table: "PlayerStatsMonthly",
                columns: new[] { "Year", "Month" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerStatsMonthly");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PlayerServerStats",
                table: "PlayerServerStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerServerStats_Year_Month",
                table: "PlayerServerStats");

            migrationBuilder.DropPrimaryKey(
                name: "PK_PlayerMapStats",
                table: "PlayerMapStats");

            migrationBuilder.DropIndex(
                name: "IX_PlayerMapStats_Year_Month",
                table: "PlayerMapStats");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "PlayerServerStats");

            migrationBuilder.DropColumn(
                name: "Year",
                table: "PlayerMapStats");

            migrationBuilder.DropColumn(
                name: "Month",
                table: "PlayerMapStats");

            migrationBuilder.RenameColumn(
                name: "Month",
                table: "PlayerServerStats",
                newName: "HighestScore");

            migrationBuilder.AddColumn<string>(
                name: "HighestScoreMapName",
                table: "PlayerServerStats",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HighestScoreRoundId",
                table: "PlayerServerStats",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HighestScoreTime",
                table: "PlayerServerStats",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_PlayerServerStats",
                table: "PlayerServerStats",
                columns: new[] { "PlayerName", "ServerGuid" });

            migrationBuilder.AddPrimaryKey(
                name: "PK_PlayerMapStats",
                table: "PlayerMapStats",
                columns: new[] { "PlayerName", "MapName", "ServerGuid" });

            migrationBuilder.CreateTable(
                name: "PlayerStatsLifetimes",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    AvgScorePerRound = table.Column<double>(type: "REAL", nullable: false),
                    FirstRoundTime = table.Column<string>(type: "TEXT", nullable: false),
                    KdRatio = table.Column<double>(type: "REAL", nullable: false),
                    KillRate = table.Column<double>(type: "REAL", nullable: false),
                    LastRoundTime = table.Column<string>(type: "TEXT", nullable: false),
                    TotalDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalKills = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalPlayTimeMinutes = table.Column<double>(type: "REAL", nullable: false),
                    TotalRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalScore = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerStatsLifetimes", x => x.PlayerName);
                });
        }
    }
}
