using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add tournament columns to Rounds table
            migrationBuilder.AddColumn<string>(
                name: "TournamentId",
                table: "Rounds",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTournamentRound",
                table: "Rounds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Create Tournaments table
            migrationBuilder.CreateTable(
                name: "Tournaments",
                columns: table => new
                {
                    TournamentId = table.Column<string>(type: "TEXT", nullable: false),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    ServerName = table.Column<string>(type: "TEXT", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    GameType = table.Column<string>(type: "TEXT", nullable: false),
                    StartTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    TotalRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    ParticipantCount = table.Column<int>(type: "INTEGER", nullable: true),
                    TournamentType = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tournaments", x => x.TournamentId);
                    table.ForeignKey(
                        name: "FK_Tournaments_Servers_ServerGuid",
                        column: x => x.ServerGuid,
                        principalTable: "Servers",
                        principalColumn: "Guid",
                        onDelete: ReferentialAction.Cascade);
                });

            // Create TournamentRounds table
            migrationBuilder.CreateTable(
                name: "TournamentRounds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TournamentId = table.Column<string>(type: "TEXT", nullable: false),
                    RoundId = table.Column<string>(type: "TEXT", nullable: false),
                    RoundNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentRounds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentRounds_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "TournamentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentRounds_Rounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "Rounds",
                        principalColumn: "RoundId",
                        onDelete: ReferentialAction.Cascade);
                });

            // Create indexes
            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_ServerGuid_StartTime",
                table: "Tournaments",
                columns: new[] { "ServerGuid", "StartTime" });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_MapName",
                table: "Tournaments",
                column: "MapName");

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_IsActive",
                table: "Tournaments",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentRounds_TournamentId",
                table: "TournamentRounds",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentRounds_RoundId",
                table: "TournamentRounds",
                column: "RoundId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentRounds");

            migrationBuilder.DropTable(
                name: "Tournaments");

            migrationBuilder.DropColumn(
                name: "TournamentId",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "IsTournamentRound",
                table: "Rounds");
        }
    }
}