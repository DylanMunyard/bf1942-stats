using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentPosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TournamentPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedByUserEmail = table.Column<string>(type: "TEXT", nullable: false),
                    PublishAt = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentPosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentPosts_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TournamentPosts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentPosts_CreatedAt",
                table: "TournamentPosts",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentPosts_CreatedByUserId",
                table: "TournamentPosts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentPosts_TournamentId",
                table: "TournamentPosts",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentPosts_TournamentId_Status_PublishAt",
                table: "TournamentPosts",
                columns: new[] { "TournamentId", "Status", "PublishAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentPosts");
        }
    }
}
