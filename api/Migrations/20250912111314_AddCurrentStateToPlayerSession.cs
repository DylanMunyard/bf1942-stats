using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class AddCurrentStateToPlayerSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CurrentPing",
                table: "PlayerSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "CurrentTeam",
                table: "PlayerSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "CurrentTeamLabel",
                table: "PlayerSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentPing",
                table: "PlayerSessions");

            migrationBuilder.DropColumn(
                name: "CurrentTeam",
                table: "PlayerSessions");

            migrationBuilder.DropColumn(
                name: "CurrentTeamLabel",
                table: "PlayerSessions");
        }
    }
}
