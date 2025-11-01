using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class AddWeekToTournamentMatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Week",
                table: "TournamentMatches",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Week",
                table: "TournamentMatches");
        }
    }
}
