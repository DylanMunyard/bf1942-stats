using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class OptimizeLastRoundsQuery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerSessions_ServerGuid",
                table: "PlayerSessions");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_ServerGuid_LastSeenTime",
                table: "PlayerSessions",
                columns: new[] { "ServerGuid", "LastSeenTime" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_ServerGuid_StartTime_MapName",
                table: "PlayerSessions",
                columns: new[] { "ServerGuid", "StartTime", "MapName" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerSessions_ServerGuid_LastSeenTime",
                table: "PlayerSessions");

            migrationBuilder.DropIndex(
                name: "IX_PlayerSessions_ServerGuid_StartTime_MapName",
                table: "PlayerSessions");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_ServerGuid",
                table: "PlayerSessions",
                column: "ServerGuid");
        }
    }
}
