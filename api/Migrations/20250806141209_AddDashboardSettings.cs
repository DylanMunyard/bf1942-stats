using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UserBuddies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    BuddyPlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserBuddies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserBuddies_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserFavoriteServers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserFavoriteServers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserFavoriteServers_Servers_ServerGuid",
                        column: x => x.ServerGuid,
                        principalTable: "Servers",
                        principalColumn: "Guid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserFavoriteServers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPlayerNames",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPlayerNames", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPlayerNames_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserBuddies_UserId_BuddyPlayerName",
                table: "UserBuddies",
                columns: new[] { "UserId", "BuddyPlayerName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserFavoriteServers_ServerGuid",
                table: "UserFavoriteServers",
                column: "ServerGuid");

            migrationBuilder.CreateIndex(
                name: "IX_UserFavoriteServers_UserId_ServerGuid",
                table: "UserFavoriteServers",
                columns: new[] { "UserId", "ServerGuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPlayerNames_UserId_PlayerName",
                table: "UserPlayerNames",
                columns: new[] { "UserId", "PlayerName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserBuddies");

            migrationBuilder.DropTable(
                name: "UserFavoriteServers");

            migrationBuilder.DropTable(
                name: "UserPlayerNames");
        }
    }
}
