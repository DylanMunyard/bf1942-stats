using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Index consideration: we did not add an index on Rounds.IsDeleted or a standalone index on
    /// PlayerSessions.IsDeleted. The common predicate is "exclude deleted" (IsDeleted=0), which
    /// matches almost all rows — low selectivity. A partial index on PlayerSessions(LastSeenTime)
    /// WHERE IsDeleted=0 is added in AddPlayerSessionsLastSeenTimeWhereNotDeletedIndex for tier/backfill.
    /// </remarks>
    public partial class AddIsDeletedToRoundAndPlayerSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "Rounds",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "PlayerSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "Rounds");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "PlayerSessions");
        }
    }
}
