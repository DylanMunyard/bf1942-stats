using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentStatusGameModeWeekDatesFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add Status and GameMode columns to Tournaments
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Tournaments",
                type: "TEXT",
                nullable: false,
                defaultValue: "draft");

            migrationBuilder.AddColumn<string>(
                name: "GameMode",
                table: "Tournaments",
                type: "TEXT",
                nullable: true);

            // Create TournamentWeekDates table
            migrationBuilder.CreateTable(
                name: "TournamentWeekDates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Week = table.Column<string>(type: "TEXT", nullable: true),
                    StartDate = table.Column<string>(type: "TEXT", nullable: false),
                    EndDate = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentWeekDates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentWeekDates_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Create TournamentFiles table
            migrationBuilder.CreateTable(
                name: "TournamentFiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TournamentId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Url = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<string>(type: "TEXT", nullable: true),
                    UploadedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentFiles_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Create indexes for TournamentWeekDates
            migrationBuilder.CreateIndex(
                name: "IX_TournamentWeekDates_TournamentId",
                table: "TournamentWeekDates",
                column: "TournamentId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentWeekDates_TournamentId_Week",
                table: "TournamentWeekDates",
                columns: new[] { "TournamentId", "Week" });

            // Create indexes for TournamentFiles
            migrationBuilder.CreateIndex(
                name: "IX_TournamentFiles_TournamentId",
                table: "TournamentFiles",
                column: "TournamentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop tables
            migrationBuilder.DropTable(
                name: "TournamentWeekDates");

            migrationBuilder.DropTable(
                name: "TournamentFiles");

            // Drop columns from Tournaments
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "GameMode",
                table: "Tournaments");
        }
    }
}
