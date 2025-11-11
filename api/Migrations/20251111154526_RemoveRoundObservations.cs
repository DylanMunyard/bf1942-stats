using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRoundObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS RoundObservations;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoundObservations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoundId = table.Column<string>(type: "TEXT", nullable: false),
                    RoundTimeRemain = table.Column<int>(type: "INTEGER", nullable: true),
                    Team1Label = table.Column<string>(type: "TEXT", nullable: true),
                    Team2Label = table.Column<string>(type: "TEXT", nullable: true),
                    Tickets1 = table.Column<int>(type: "INTEGER", nullable: true),
                    Tickets2 = table.Column<int>(type: "INTEGER", nullable: true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoundObservations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoundObservations_RoundId",
                table: "RoundObservations",
                column: "RoundId");

            migrationBuilder.CreateIndex(
                name: "IX_RoundObservations_RoundId_Timestamp",
                table: "RoundObservations",
                columns: new[] { "RoundId", "Timestamp" });
        }
    }
}
