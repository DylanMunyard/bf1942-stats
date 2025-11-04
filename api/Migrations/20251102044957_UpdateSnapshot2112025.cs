using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSnapshot2112025 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TournamentMatchResults_MapId",
                table: "TournamentMatchResults");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_MapId",
                table: "TournamentMatchResults",
                column: "MapId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TournamentMatchResults_MapId",
                table: "TournamentMatchResults");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatchResults_MapId",
                table: "TournamentMatchResults",
                column: "MapId");
        }
    }
}
