using api.PlayerTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    [DbContext(typeof(PlayerTrackerDbContext))]
    [Migration("20260119123000_AddPlacementLeaderboardIndexes")]
    public partial class AddPlacementLeaderboardIndexes : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS IX_PlayerAchievements_Placement_Server_AchievedAt_Player_Tier
ON PlayerAchievements (ServerGuid, AchievedAt, PlayerName, Tier)
WHERE AchievementType = 'round_placement';");

            migrationBuilder.Sql(@"
CREATE INDEX IF NOT EXISTS IX_PlayerAchievements_Placement_Weighted_TotalPlayers
ON PlayerAchievements (
    ServerGuid,
    AchievedAt,
    PlayerName,
    Tier,
    CAST(json_extract(Metadata, '$.TotalPlayers') AS INTEGER)
)
WHERE AchievementType = 'round_placement';");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PlayerAchievements_Placement_Weighted_TotalPlayers;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS IX_PlayerAchievements_Placement_Server_AchievedAt_Player_Tier;");
        }
    }
}
