using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDisplayNameFromUserBuddy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "UserBuddies");

            migrationBuilder.CreateIndex(
                name: "IX_UserPlayerNames_PlayerName",
                table: "UserPlayerNames",
                column: "PlayerName");

            migrationBuilder.CreateIndex(
                name: "IX_UserBuddies_BuddyPlayerName",
                table: "UserBuddies",
                column: "BuddyPlayerName");

            migrationBuilder.AddForeignKey(
                name: "FK_UserBuddies_Players_BuddyPlayerName",
                table: "UserBuddies",
                column: "BuddyPlayerName",
                principalTable: "Players",
                principalColumn: "Name",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_UserPlayerNames_Players_PlayerName",
                table: "UserPlayerNames",
                column: "PlayerName",
                principalTable: "Players",
                principalColumn: "Name",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_UserBuddies_Players_BuddyPlayerName",
                table: "UserBuddies");

            migrationBuilder.DropForeignKey(
                name: "FK_UserPlayerNames_Players_PlayerName",
                table: "UserPlayerNames");

            migrationBuilder.DropIndex(
                name: "IX_UserPlayerNames_PlayerName",
                table: "UserPlayerNames");

            migrationBuilder.DropIndex(
                name: "IX_UserBuddies_BuddyPlayerName",
                table: "UserBuddies");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "UserBuddies",
                type: "TEXT",
                nullable: true);
        }
    }
}
