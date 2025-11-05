using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRoundIdFromTournamentMatchMapAndUpdateRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SQLite requires disabling foreign keys outside of a transaction
            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchMaps_Rounds_RoundId",
                table: "TournamentMatchMaps");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchResults_Rounds_RoundId",
                table: "TournamentMatchResults");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatchResults_MapId",
                table: "TournamentMatchResults");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatchMaps_RoundId",
                table: "TournamentMatchMaps");

            migrationBuilder.DropColumn(
                name: "RoundId",
                table: "TournamentMatchMaps");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_MapId",
                table: "TournamentMatchResults",
                column: "MapId");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchResults_Rounds_RoundId",
                table: "TournamentMatchResults",
                column: "RoundId",
                principalTable: "Rounds",
                principalColumn: "RoundId",
                onDelete: ReferentialAction.SetNull);

            // Re-enable foreign keys
            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite requires disabling foreign keys outside of a transaction
            migrationBuilder.Sql("PRAGMA foreign_keys = 0;", suppressTransaction: true);

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchResults_Rounds_RoundId",
                table: "TournamentMatchResults");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatchResults_MapId",
                table: "TournamentMatchResults");

            migrationBuilder.AddColumn<string>(
                name: "RoundId",
                table: "TournamentMatchMaps",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_MapId",
                table: "TournamentMatchResults",
                column: "MapId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchMaps_RoundId",
                table: "TournamentMatchMaps",
                column: "RoundId");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchMaps_Rounds_RoundId",
                table: "TournamentMatchMaps",
                column: "RoundId",
                principalTable: "Rounds",
                principalColumn: "RoundId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchResults_Rounds_RoundId",
                table: "TournamentMatchResults",
                column: "RoundId",
                principalTable: "Rounds",
                principalColumn: "RoundId",
                onDelete: ReferentialAction.Cascade);

            // Re-enable foreign keys
            migrationBuilder.Sql("PRAGMA foreign_keys = 1;", suppressTransaction: true);
        }
    }
}
