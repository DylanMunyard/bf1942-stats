using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddHeroImageToTournament : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "HeroImage",
                table: "Tournaments",
                type: "BLOB",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeroImageContentType",
                table: "Tournaments",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HeroImage",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "HeroImageContentType",
                table: "Tournaments");
        }
    }
}
