using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddClickHouseMigrationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HourlyActivityPatterns",
                columns: table => new
                {
                    Game = table.Column<string>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    HourOfDay = table.Column<int>(type: "INTEGER", nullable: false),
                    UniquePlayersAvg = table.Column<double>(type: "REAL", nullable: false),
                    TotalRoundsAvg = table.Column<double>(type: "REAL", nullable: false),
                    AvgRoundDuration = table.Column<double>(type: "REAL", nullable: false),
                    PeriodType = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HourlyActivityPatterns", x => new { x.Game, x.DayOfWeek, x.HourOfDay });
                });

            migrationBuilder.CreateTable(
                name: "HourlyPlayerPredictions",
                columns: table => new
                {
                    Game = table.Column<string>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    HourOfDay = table.Column<int>(type: "INTEGER", nullable: false),
                    PredictedPlayers = table.Column<double>(type: "REAL", nullable: false),
                    DataPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HourlyPlayerPredictions", x => new { x.Game, x.DayOfWeek, x.HourOfDay });
                });

            migrationBuilder.CreateTable(
                name: "MapGlobalAverages",
                columns: table => new
                {
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    AvgKillRate = table.Column<double>(type: "REAL", nullable: false),
                    AvgScoreRate = table.Column<double>(type: "REAL", nullable: false),
                    SampleCount = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapGlobalAverages", x => new { x.MapName, x.ServerGuid });
                });

            migrationBuilder.CreateTable(
                name: "PlayerBestScores",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    Period = table.Column<string>(type: "TEXT", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    FinalScore = table.Column<int>(type: "INTEGER", nullable: false),
                    FinalKills = table.Column<int>(type: "INTEGER", nullable: false),
                    FinalDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    RoundEndTime = table.Column<string>(type: "TEXT", nullable: false),
                    RoundId = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerBestScores", x => new { x.PlayerName, x.Period, x.Rank });
                });

            migrationBuilder.CreateTable(
                name: "PlayerDailyStats",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    DailyKills = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyScore = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyPlayTimeMinutes = table.Column<double>(type: "REAL", nullable: false),
                    DailyRounds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDailyStats", x => new { x.PlayerName, x.Date });
                });

            migrationBuilder.CreateTable(
                name: "PlayerMapStats",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    TotalRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalKills = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalScore = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalPlayTimeMinutes = table.Column<double>(type: "REAL", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMapStats", x => new { x.PlayerName, x.MapName, x.ServerGuid });
                });

            migrationBuilder.CreateTable(
                name: "PlayerMilestones",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    Milestone = table.Column<int>(type: "INTEGER", nullable: false),
                    AchievedAt = table.Column<string>(type: "TEXT", nullable: false),
                    TotalKillsAtMilestone = table.Column<int>(type: "INTEGER", nullable: false),
                    DaysToAchieve = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMilestones", x => new { x.PlayerName, x.Milestone });
                });

            migrationBuilder.CreateTable(
                name: "PlayerServerStats",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    TotalRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalKills = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalScore = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalPlayTimeMinutes = table.Column<double>(type: "REAL", nullable: false),
                    HighestScore = table.Column<int>(type: "INTEGER", nullable: false),
                    HighestScoreRoundId = table.Column<string>(type: "TEXT", nullable: true),
                    HighestScoreMapName = table.Column<string>(type: "TEXT", nullable: true),
                    HighestScoreTime = table.Column<string>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerServerStats", x => new { x.PlayerName, x.ServerGuid });
                });

            migrationBuilder.CreateTable(
                name: "PlayerStatsLifetimes",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
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
                    table.PrimaryKey("PK_PlayerStatsLifetimes", x => x.PlayerName);
                });

            migrationBuilder.CreateTable(
                name: "ServerHourlyPatterns",
                columns: table => new
                {
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    HourOfDay = table.Column<int>(type: "INTEGER", nullable: false),
                    AvgPlayers = table.Column<double>(type: "REAL", nullable: false),
                    MinPlayers = table.Column<double>(type: "REAL", nullable: false),
                    Q25Players = table.Column<double>(type: "REAL", nullable: false),
                    MedianPlayers = table.Column<double>(type: "REAL", nullable: false),
                    Q75Players = table.Column<double>(type: "REAL", nullable: false),
                    Q90Players = table.Column<double>(type: "REAL", nullable: false),
                    MaxPlayers = table.Column<double>(type: "REAL", nullable: false),
                    DataPoints = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerHourlyPatterns", x => new { x.ServerGuid, x.DayOfWeek, x.HourOfDay });
                });

            migrationBuilder.CreateTable(
                name: "ServerLeaderboardEntries",
                columns: table => new
                {
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    Period = table.Column<string>(type: "TEXT", nullable: false),
                    RankingType = table.Column<string>(type: "TEXT", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<double>(type: "REAL", nullable: false),
                    TotalRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerLeaderboardEntries", x => new { x.ServerGuid, x.Period, x.RankingType, x.Rank });
                });

            migrationBuilder.CreateTable(
                name: "ServerOnlineCounts",
                columns: table => new
                {
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    HourTimestamp = table.Column<string>(type: "TEXT", nullable: false),
                    Game = table.Column<string>(type: "TEXT", nullable: false),
                    AvgPlayers = table.Column<double>(type: "REAL", nullable: false),
                    PeakPlayers = table.Column<int>(type: "INTEGER", nullable: false),
                    SampleCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerOnlineCounts", x => new { x.ServerGuid, x.HourTimestamp });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDailyStats_Date",
                table: "PlayerDailyStats",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerMapStats_MapName",
                table: "PlayerMapStats",
                column: "MapName");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerServerStats_ServerGuid",
                table: "PlayerServerStats",
                column: "ServerGuid");

            migrationBuilder.CreateIndex(
                name: "IX_ServerLeaderboardEntries_PlayerName",
                table: "ServerLeaderboardEntries",
                column: "PlayerName");

            migrationBuilder.CreateIndex(
                name: "IX_ServerOnlineCounts_Game_HourTimestamp",
                table: "ServerOnlineCounts",
                columns: new[] { "Game", "HourTimestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_ServerOnlineCounts_HourTimestamp",
                table: "ServerOnlineCounts",
                column: "HourTimestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HourlyActivityPatterns");

            migrationBuilder.DropTable(
                name: "HourlyPlayerPredictions");

            migrationBuilder.DropTable(
                name: "MapGlobalAverages");

            migrationBuilder.DropTable(
                name: "PlayerBestScores");

            migrationBuilder.DropTable(
                name: "PlayerDailyStats");

            migrationBuilder.DropTable(
                name: "PlayerMapStats");

            migrationBuilder.DropTable(
                name: "PlayerMilestones");

            migrationBuilder.DropTable(
                name: "PlayerServerStats");

            migrationBuilder.DropTable(
                name: "PlayerStatsLifetimes");

            migrationBuilder.DropTable(
                name: "ServerHourlyPatterns");

            migrationBuilder.DropTable(
                name: "ServerLeaderboardEntries");

            migrationBuilder.DropTable(
                name: "ServerOnlineCounts");
        }
    }
}
