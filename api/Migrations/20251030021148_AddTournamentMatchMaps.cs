using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentMatchMaps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatches_Rounds_RoundId",
                table: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_RoundId",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "MapName",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "RoundId",
                table: "TournamentMatches");

            migrationBuilder.CreateTable(
                name: "TournamentMatchMaps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MatchId = table.Column<int>(type: "INTEGER", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    MapOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentMatchMaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentMatchMaps_Rounds_RoundId",
                        column: x => x.RoundId,
                        principalTable: "Rounds",
                        principalColumn: "RoundId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TournamentMatchMaps_TournamentMatches_MatchId",
                        column: x => x.MatchId,
                        principalTable: "TournamentMatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchMaps_MatchId",
                table: "TournamentMatchMaps",
                column: "MatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchMaps_MatchId_MapOrder",
                table: "TournamentMatchMaps",
                columns: new[] { "MatchId", "MapOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchMaps_RoundId",
                table: "TournamentMatchMaps",
                column: "RoundId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentMatchMaps");

            migrationBuilder.AddColumn<string>(
                name: "MapName",
                table: "TournamentMatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "RoundId",
                table: "TournamentMatches",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_RoundId",
                table: "TournamentMatches",
                column: "RoundId");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatches_Rounds_RoundId",
                table: "TournamentMatches",
                column: "RoundId",
                principalTable: "Rounds",
                principalColumn: "RoundId",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
