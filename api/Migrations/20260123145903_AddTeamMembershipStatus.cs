using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamMembershipStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default to Approved (1) for existing records
            migrationBuilder.AddColumn<int>(
                name: "MembershipStatus",
                table: "TournamentTeamPlayers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);  // Approved = 1
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MembershipStatus",
                table: "TournamentTeamPlayers");
        }
    }
}
