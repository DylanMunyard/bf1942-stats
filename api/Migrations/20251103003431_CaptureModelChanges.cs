using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class CaptureModelChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrimaryColour",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "SecondaryColour",
                table: "Tournaments");

            migrationBuilder.AddColumn<int>(
                name: "ThemeId",
                table: "Tournaments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentTheme",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BackgroundColour = table.Column<string>(type: "TEXT", nullable: true),
                    TextColour = table.Column<string>(type: "TEXT", nullable: true),
                    AccentColour = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentTheme", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_ThemeId",
                table: "Tournaments",
                column: "ThemeId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Tournaments_TournamentTheme_ThemeId",
                table: "Tournaments",
                column: "ThemeId",
                principalTable: "TournamentTheme",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tournaments_TournamentTheme_ThemeId",
                table: "Tournaments");

            migrationBuilder.DropTable(
                name: "TournamentTheme");

            migrationBuilder.DropIndex(
                name: "IX_Tournaments_ThemeId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "ThemeId",
                table: "Tournaments");

            migrationBuilder.AddColumn<string>(
                name: "PrimaryColour",
                table: "Tournaments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryColour",
                table: "Tournaments",
                type: "TEXT",
                nullable: true);
        }
    }
}
