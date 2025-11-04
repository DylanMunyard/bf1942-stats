using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class PendingModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoundListItems",
                columns: table => new
                {
                    RoundId = table.Column<string>(type: "TEXT", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", nullable: false),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    GameType = table.Column<string>(type: "TEXT", nullable: true),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    ParticipantCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalSessions = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.CreateTable(
                name: "ServerBestScoreRaws",
                columns: table => new
                {
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", nullable: false),
                    BestScore = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalKills = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayTimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    BestScoreDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                });

            migrationBuilder.Sql(@"
                CREATE INDEX IF NOT EXISTS IX_PlayerSessions_IsActive_LastSeenTime 
                ON PlayerSessions (IsActive, LastSeenTime)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoundListItems");

            migrationBuilder.DropTable(
                name: "ServerBestScoreRaws");

            migrationBuilder.DropIndex(
                name: "IX_PlayerSessions_IsActive_LastSeenTime",
                table: "PlayerSessions");
        }
    }
}
