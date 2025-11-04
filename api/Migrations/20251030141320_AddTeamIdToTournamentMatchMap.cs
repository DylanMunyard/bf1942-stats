using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamIdToTournamentMatchMap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TeamId",
                table: "TournamentMatchMaps",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchMaps_TeamId",
                table: "TournamentMatchMaps",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchMaps_TournamentTeams_TeamId",
                table: "TournamentMatchMaps",
                column: "TeamId",
                principalTable: "TournamentTeams",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchMaps_TournamentTeams_TeamId",
                table: "TournamentMatchMaps");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatchMaps_TeamId",
                table: "TournamentMatchMaps");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "TournamentMatchMaps");
        }
    }
}
