using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentRankingColumnsForMatchesAndTickets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Losses",
                table: "TournamentTeamRankings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MatchesPlayed",
                table: "TournamentTeamRankings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Points",
                table: "TournamentTeamRankings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TicketsAgainst",
                table: "TournamentTeamRankings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TicketsFor",
                table: "TournamentTeamRankings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Ties",
                table: "TournamentTeamRankings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Victories",
                table: "TournamentTeamRankings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Losses",
                table: "TournamentTeamRankings");

            migrationBuilder.DropColumn(
                name: "MatchesPlayed",
                table: "TournamentTeamRankings");

            migrationBuilder.DropColumn(
                name: "Points",
                table: "TournamentTeamRankings");

            migrationBuilder.DropColumn(
                name: "TicketsAgainst",
                table: "TournamentTeamRankings");

            migrationBuilder.DropColumn(
                name: "TicketsFor",
                table: "TournamentTeamRankings");

            migrationBuilder.DropColumn(
                name: "Ties",
                table: "TournamentTeamRankings");

            migrationBuilder.DropColumn(
                name: "Victories",
                table: "TournamentTeamRankings");
        }
    }
}
