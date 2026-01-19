using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamRegistrationFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LeaderUserId",
                table: "TournamentTeams",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tag",
                table: "TournamentTeams",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTeamLeader",
                table: "TournamentTeamPlayers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "JoinedAt",
                table: "TournamentTeamPlayers",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "RulesAcknowledged",
                table: "TournamentTeamPlayers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "RulesAcknowledgedAt",
                table: "TournamentTeamPlayers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "TournamentTeamPlayers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentTeams_LeaderUserId",
                table: "TournamentTeams",
                column: "LeaderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentTeamPlayers_UserId",
                table: "TournamentTeamPlayers",
                column: "UserId");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentTeamPlayers_Users_UserId",
                table: "TournamentTeamPlayers",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentTeams_Users_LeaderUserId",
                table: "TournamentTeams",
                column: "LeaderUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentTeamPlayers_Users_UserId",
                table: "TournamentTeamPlayers");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentTeams_Users_LeaderUserId",
                table: "TournamentTeams");

            migrationBuilder.DropIndex(
                name: "IX_TournamentTeams_LeaderUserId",
                table: "TournamentTeams");

            migrationBuilder.DropIndex(
                name: "IX_TournamentTeamPlayers_UserId",
                table: "TournamentTeamPlayers");

            migrationBuilder.DropColumn(
                name: "LeaderUserId",
                table: "TournamentTeams");

            migrationBuilder.DropColumn(
                name: "Tag",
                table: "TournamentTeams");

            migrationBuilder.DropColumn(
                name: "IsTeamLeader",
                table: "TournamentTeamPlayers");

            migrationBuilder.DropColumn(
                name: "JoinedAt",
                table: "TournamentTeamPlayers");

            migrationBuilder.DropColumn(
                name: "RulesAcknowledged",
                table: "TournamentTeamPlayers");

            migrationBuilder.DropColumn(
                name: "RulesAcknowledgedAt",
                table: "TournamentTeamPlayers");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "TournamentTeamPlayers");
        }
    }
}
