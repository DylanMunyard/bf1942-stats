using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentCommunityLogoAndRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "CommunityLogo",
                table: "Tournaments",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CommunityLogoContentType",
                table: "Tournaments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Rules",
                table: "Tournaments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommunityLogo",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "CommunityLogoContentType",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "Rules",
                table: "Tournaments");
        }
    }
}
