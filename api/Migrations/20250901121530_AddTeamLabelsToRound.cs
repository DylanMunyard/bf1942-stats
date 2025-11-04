using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamLabelsToRound : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Team1Label",
                table: "RoundListItem",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Team2Label",
                table: "RoundListItem",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RoundId",
                table: "PlayerSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoundObservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoundId = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Tickets1 = table.Column<int>(type: "INTEGER", nullable: true),
                    Tickets2 = table.Column<int>(type: "INTEGER", nullable: true),
                    Team1Label = table.Column<string>(type: "TEXT", nullable: true),
                    Team2Label = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundObservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Rounds",
                columns: table => new
                {
                    RoundId = table.Column<string>(type: "TEXT", nullable: false),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    GameType = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    DurationMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    ParticipantCount = table.Column<int>(type: "INTEGER", nullable: true),
                    Tickets1 = table.Column<int>(type: "INTEGER", nullable: true),
                    Tickets2 = table.Column<int>(type: "INTEGER", nullable: true),
                    Team1Label = table.Column<string>(type: "TEXT", nullable: true),
                    Team2Label = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rounds", x => x.RoundId);
                    table.CheckConstraint("CK_Round_EndTime", "EndTime IS NULL OR EndTime >= StartTime");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_RoundId",
                table: "PlayerSessions",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_RoundId_PlayerName",
                table: "PlayerSessions",
                columns: new[] { "RoundId", "PlayerName" });

            migrationBuilder.CreateIndex(
                name: "IX_RoundObservations_RoundId",
                table: "RoundObservations",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundObservations_RoundId_Timestamp",
                table: "RoundObservations",
                columns: new[] { "RoundId", "Timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_IsActive",
                table: "Rounds",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_MapName",
                table: "Rounds",
                column: "MapName");

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ServerGuid",
                table: "Rounds",
                column: "ServerGuid",
                unique: true,
                filter: "IsActive = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ServerGuid_EndTime",
                table: "Rounds",
                columns: new[] { "ServerGuid", "EndTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Rounds_ServerGuid_StartTime",
                table: "Rounds",
                columns: new[] { "ServerGuid", "StartTime" });

            migrationBuilder.AddForeignKey(
                name: "FK_PlayerSessions_Rounds_RoundId",
                table: "PlayerSessions",
                column: "RoundId",
                principalTable: "Rounds",
                principalColumn: "RoundId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PlayerSessions_Rounds_RoundId",
                table: "PlayerSessions");

            migrationBuilder.DropTable(
                name: "RoundObservations");

            migrationBuilder.DropTable(
                name: "Rounds");

            migrationBuilder.DropIndex(
                name: "IX_PlayerSessions_RoundId",
                table: "PlayerSessions");

            migrationBuilder.DropIndex(
                name: "IX_PlayerSessions_RoundId_PlayerName",
                table: "PlayerSessions");

            migrationBuilder.DropColumn(
                name: "Team1Label",
                table: "RoundListItem");

            migrationBuilder.DropColumn(
                name: "Team2Label",
                table: "RoundListItem");

            migrationBuilder.DropColumn(
                name: "RoundId",
                table: "PlayerSessions");
        }
    }
}
