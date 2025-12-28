using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class RemovePlayerDailyStats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerDailyStats");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerDailyStats",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    Date = table.Column<string>(type: "TEXT", nullable: false),
                    DailyDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyKills = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyPlayTimeMinutes = table.Column<double>(type: "REAL", nullable: false),
                    DailyRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    DailyScore = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerDailyStats", x => new { x.PlayerName, x.Date });
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerDailyStats_Date",
                table: "PlayerDailyStats",
                column: "Date");
        }
    }
}
