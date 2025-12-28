using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class ClickHouseMigrationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "TotalDeaths",
                table: "ServerLeaderboardEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalKills",
                table: "ServerLeaderboardEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "TotalPlayTimeMinutes",
                table: "ServerLeaderboardEntries",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<int>(
                name: "TotalScore",
                table: "ServerLeaderboardEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TotalDeaths",
                table: "ServerLeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "TotalKills",
                table: "ServerLeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "TotalPlayTimeMinutes",
                table: "ServerLeaderboardEntries");

            migrationBuilder.DropColumn(
                name: "TotalScore",
                table: "ServerLeaderboardEntries");
        }
    }
}
