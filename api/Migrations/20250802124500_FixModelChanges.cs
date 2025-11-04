using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class FixModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "ServerBestScoreRaws",
                newName: "ServerBestScoreRaw");

            migrationBuilder.RenameTable(
                name: "RoundListItems",
                newName: "RoundListItem");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(
                name: "ServerBestScoreRaw",
                newName: "ServerBestScoreRaws");

            migrationBuilder.RenameTable(
                name: "RoundListItem",
                newName: "RoundListItems");
        }
    }
}
