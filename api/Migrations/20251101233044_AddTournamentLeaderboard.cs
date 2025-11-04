using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentLeaderboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TournamentMatchResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                    MatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    MapId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundId = table.Column<string>(type: "TEXT", nullable: false),
                    Week = table.Column<string>(type: "TEXT", nullable: true),
                    Team1Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Team2Id = table.Column<int>(type: "INTEGER", nullable: false),
                    WinningTeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    Team1Tickets = table.Column<int>(type: "INTEGER", nullable: false),
                    Team2Tickets = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentMatchResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentMatchResults_Rounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "Rounds",
                        principalColumn: "RoundId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentMatchResults_TournamentMatchMaps_MapId",
                        column: x => x.MapId,
                        principalTable: "TournamentMatchMaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentMatchResults_TournamentMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "TournamentMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentMatchResults_TournamentTeams_Team1Id",
                        column: x => x.Team1Id,
                        principalTable: "TournamentTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatchResults_TournamentTeams_Team2Id",
                        column: x => x.Team2Id,
                        principalTable: "TournamentTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatchResults_TournamentTeams_WinningTeamId",
                        column: x => x.WinningTeamId,
                        principalTable: "TournamentTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatchResults_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TournamentTeamRankings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                    TeamId = table.Column<int>(type: "INTEGER", nullable: false),
                    Week = table.Column<string>(type: "TEXT", nullable: true),
                    RoundsWon = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundsTied = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundsLost = table.Column<int>(type: "INTEGER", nullable: false),
                    TicketDifferential = table.Column<int>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentTeamRankings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentTeamRankings_TournamentTeams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "TournamentTeams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentTeamRankings_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_MapId",
                table: "TournamentMatchResults",
                column: "MapId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_MatchId",
                table: "TournamentMatchResults",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_RoundId",
                table: "TournamentMatchResults",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_Team1Id",
                table: "TournamentMatchResults",
                column: "Team1Id");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_Team2Id",
                table: "TournamentMatchResults",
                column: "Team2Id");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_TournamentId",
                table: "TournamentMatchResults",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_TournamentId_Week",
                table: "TournamentMatchResults",
                columns: new[] { "TournamentId", "Week" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_WinningTeamId",
                table: "TournamentMatchResults",
                column: "WinningTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentTeamRankings_TeamId",
                table: "TournamentTeamRankings",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentTeamRankings_TournamentId",
                table: "TournamentTeamRankings",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentTeamRankings_TournamentId_TeamId_Week",
                table: "TournamentTeamRankings",
                columns: new[] { "TournamentId", "TeamId", "Week" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentTeamRankings_TournamentId_Week",
                table: "TournamentTeamRankings",
                columns: new[] { "TournamentId", "Week" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentMatchResults");

            migrationBuilder.DropTable(
                name: "TournamentTeamRankings");
        }
    }
}
