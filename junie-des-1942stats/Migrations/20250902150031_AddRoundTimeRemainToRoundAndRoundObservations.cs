using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class AddRoundTimeRemainToRoundAndRoundObservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "RoundTimeRemain",
                table: "Rounds",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoundTimeRemain",
                table: "RoundObservations",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RoundTimeRemain",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "RoundTimeRemain",
                table: "RoundObservations");
        }
    }
}
