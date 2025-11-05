using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class AddServerGuidToTournament : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ServerGuid",
                table: "Tournaments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_ServerGuid",
                table: "Tournaments",
                column: "ServerGuid");

            migrationBuilder.AddForeignKey(
                name: "FK_Tournaments_Servers_ServerGuid",
                table: "Tournaments",
                column: "ServerGuid",
                principalTable: "Servers",
                principalColumn: "Guid",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tournaments_Servers_ServerGuid",
                table: "Tournaments");

            migrationBuilder.DropIndex(
                name: "IX_Tournaments_ServerGuid",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "ServerGuid",
                table: "Tournaments");
        }
    }
}
