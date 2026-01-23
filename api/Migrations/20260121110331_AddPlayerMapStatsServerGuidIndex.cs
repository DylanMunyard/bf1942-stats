using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerMapStatsServerGuidIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add composite index for ServerGuid + MapName to optimize queries like:
            // SELECT ... FROM PlayerMapStats WHERE ServerGuid = @p0 AND MapName IN (...)
            // This reduces full table scans from millions of rows to targeted index seeks
            migrationBuilder.CreateIndex(
                name: "IX_PlayerMapStats_ServerGuid_MapName",
                table: "PlayerMapStats",
                columns: new[] { "ServerGuid", "MapName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerMapStats_ServerGuid_MapName",
                table: "PlayerMapStats");
        }
    }
}
