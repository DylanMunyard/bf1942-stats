using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentImageIndexAndImagePath : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImagePath",
                table: "TournamentMatchMaps",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentImageIndices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FolderPath = table.Column<string>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    RelativeImagePath = table.Column<string>(type: "TEXT", nullable: false),
                    ThumbnailData = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ImageWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    FileSize = table.Column<long>(type: "INTEGER", nullable: false),
                    IndexedAt = table.Column<string>(type: "TEXT", nullable: false),
                    FileLastModified = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentImageIndices", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentImageIndices_FolderPath",
                table: "TournamentImageIndices",
                column: "FolderPath");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentImageIndices_FolderPath_FileName",
                table: "TournamentImageIndices",
                columns: new[] { "FolderPath", "FileName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentImageIndices");

            migrationBuilder.DropColumn(
                name: "ImagePath",
                table: "TournamentMatchMaps");
        }
    }
}
