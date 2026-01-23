using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class RemovePlayerMilestones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlayerMilestones");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlayerMilestones",
                columns: table => new
                {
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    Milestone = table.Column<int>(type: "INTEGER", nullable: false),
                    AchievedAt = table.Column<string>(type: "TEXT", nullable: false),
                    DaysToAchieve = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalKillsAtMilestone = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerMilestones", x => new { x.PlayerName, x.Milestone });
                });
        }
    }
}
