using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class ServerRankingsByMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServerPlayerRankings_ServerGuid_PlayerName",
                table: "ServerPlayerRankings");

            migrationBuilder.DropColumn(
                name: "LastUpdated",
                table: "ServerPlayerRankings");

            migrationBuilder.RenameColumn(
                name: "HighestScore",
                table: "ServerPlayerRankings",
                newName: "Year");

            migrationBuilder.AddColumn<int>(
                name: "Month",
                table: "ServerPlayerRankings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalScore",
                table: "ServerPlayerRankings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ServerPlayerRankings_ServerGuid_PlayerName_Year_Month",
                table: "ServerPlayerRankings",
                columns: new[] { "ServerGuid", "PlayerName", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServerPlayerRankings_ServerGuid_PlayerName_Year_Month",
                table: "ServerPlayerRankings");

            migrationBuilder.DropColumn(
                name: "Month",
                table: "ServerPlayerRankings");

            migrationBuilder.DropColumn(
                name: "TotalScore",
                table: "ServerPlayerRankings");

            migrationBuilder.RenameColumn(
                name: "Year",
                table: "ServerPlayerRankings",
                newName: "HighestScore");

            migrationBuilder.AddColumn<DateTime>(
                name: "LastUpdated",
                table: "ServerPlayerRankings",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_ServerPlayerRankings_ServerGuid_PlayerName",
                table: "ServerPlayerRankings",
                columns: new[] { "ServerGuid", "PlayerName" },
                unique: true);
        }
    }
}
