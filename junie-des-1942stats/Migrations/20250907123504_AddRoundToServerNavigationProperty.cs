using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class AddRoundToServerNavigationProperty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_Rounds_Servers_ServerGuid",
                table: "Rounds",
                column: "ServerGuid",
                principalTable: "Servers",
                principalColumn: "Guid",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rounds_Servers_ServerGuid",
                table: "Rounds");
        }
    }
}
