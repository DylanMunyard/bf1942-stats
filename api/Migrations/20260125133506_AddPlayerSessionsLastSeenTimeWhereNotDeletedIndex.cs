using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerSessionsLastSeenTimeWhereNotDeletedIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PlayerSessions_LastSeenTime_WhereNotDeleted",
                table: "PlayerSessions",
                column: "LastSeenTime",
                filter: "IsDeleted = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerSessions_LastSeenTime_WhereNotDeleted",
                table: "PlayerSessions");
        }
    }
}
