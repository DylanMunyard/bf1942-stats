using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddServerRankings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServerPlayerRankings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerGuid = table.Column<string>(type: "TEXT", nullable: false),
                    PlayerName = table.Column<string>(type: "TEXT", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: false),
                    HighestScore = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalKills = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalDeaths = table.Column<int>(type: "INTEGER", nullable: false),
                    KDRatio = table.Column<double>(type: "REAL", nullable: false),
                    TotalPlayTimeMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServerPlayerRankings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServerPlayerRankings_Players_PlayerName",
                        column: x => x.PlayerName,
                        principalTable: "Players",
                        principalColumn: "Name",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ServerPlayerRankings_Servers_ServerGuid",
                        column: x => x.ServerGuid,
                        principalTable: "Servers",
                        principalColumn: "Guid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServerPlayerRankings_PlayerName",
                table: "ServerPlayerRankings",
                column: "PlayerName");

            migrationBuilder.CreateIndex(
                name: "IX_ServerPlayerRankings_ServerGuid_PlayerName",
                table: "ServerPlayerRankings",
                columns: new[] { "ServerGuid", "PlayerName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServerPlayerRankings_ServerGuid_Rank",
                table: "ServerPlayerRankings",
                columns: new[] { "ServerGuid", "Rank" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServerPlayerRankings");
        }
    }
}
