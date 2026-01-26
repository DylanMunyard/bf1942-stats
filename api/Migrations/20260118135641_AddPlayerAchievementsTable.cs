using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerAchievementsTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerAchievements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    AchievementType = table.Column<string>(type: "TEXT", nullable: false),
                    AchievementId = table.Column<string>(type: "TEXT", nullable: false),
                    AchievementName = table.Column<string>(type: "TEXT", nullable: false),
                    Tier = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<int>(type: "INTEGER", nullable: false),
                    AchievedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ProcessedAt = table.Column<string>(type: "TEXT", nullable: false),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    MapName = table.Column<string>(type: "TEXT", nullable: false),
                    RoundId = table.Column<string>(type: "TEXT", nullable: false),
                    Metadata = table.Column<string>(type: "TEXT", nullable: true),
                    Game = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerAchievements", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievements_AchievedAt",
                table: "PlayerAchievements",
                column: "AchievedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievements_AchievementId",
                table: "PlayerAchievements",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievements_AchievementType",
                table: "PlayerAchievements",
                column: "AchievementType");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievements_MapName",
                table: "PlayerAchievements",
                column: "MapName");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievements_PlayerName_AchievedAt",
                table: "PlayerAchievements",
                columns: new[] { "PlayerName", "AchievedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievements_PlayerName_AchievementId_AchievedAt",
                table: "PlayerAchievements",
                columns: new[] { "PlayerName", "AchievementId", "AchievedAt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerAchievements_ServerGuid",
                table: "PlayerAchievements",
                column: "ServerGuid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerAchievements");
        }
    }
}
