using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class OptimizePlayerStatsQuery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add covering index for player stats query optimization
            // This index covers the common pattern: filter by PlayerName + ServerGuid, then join to get SessionIds
            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_PlayerName_ServerGuid_SessionId",
                table: "PlayerSessions",
                columns: new[] { "PlayerName", "ServerGuid", "SessionId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerSessions_PlayerName_ServerGuid_SessionId",
                table: "PlayerSessions");
        }
    }
}
