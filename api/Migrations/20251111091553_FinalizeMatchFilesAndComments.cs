using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace api.Migrations
{
    /// <inheritdoc />
    public partial class FinalizeMatchFilesAndComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchComment_TournamentMatches_MatchId",
                table: "TournamentMatchComment");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchComment_Users_CreatedByUserId",
                table: "TournamentMatchComment");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchFile_TournamentMatches_MatchId",
                table: "TournamentMatchFile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TournamentMatchFile",
                table: "TournamentMatchFile");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TournamentMatchComment",
                table: "TournamentMatchComment");

            migrationBuilder.RenameTable(
                name: "TournamentMatchFile",
                newName: "TournamentMatchFiles");

            migrationBuilder.RenameTable(
                name: "TournamentMatchComment",
                newName: "TournamentMatchComments");

            migrationBuilder.RenameIndex(
                name: "IX_TournamentMatchFile_MatchId",
                table: "TournamentMatchFiles",
                newName: "IX_TournamentMatchFiles_MatchId");

            migrationBuilder.RenameIndex(
                name: "IX_TournamentMatchComment_MatchId",
                table: "TournamentMatchComments",
                newName: "IX_TournamentMatchComments_MatchId");

            migrationBuilder.RenameIndex(
                name: "IX_TournamentMatchComment_CreatedByUserId",
                table: "TournamentMatchComments",
                newName: "IX_TournamentMatchComments_CreatedByUserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TournamentMatchFiles",
                table: "TournamentMatchFiles",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TournamentMatchComments",
                table: "TournamentMatchComments",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchComments_TournamentMatches_MatchId",
                table: "TournamentMatchComments",
                column: "MatchId",
                principalTable: "TournamentMatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchComments_Users_CreatedByUserId",
                table: "TournamentMatchComments",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchFiles_TournamentMatches_MatchId",
                table: "TournamentMatchFiles",
                column: "MatchId",
                principalTable: "TournamentMatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchComments_TournamentMatches_MatchId",
                table: "TournamentMatchComments");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchComments_Users_CreatedByUserId",
                table: "TournamentMatchComments");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatchFiles_TournamentMatches_MatchId",
                table: "TournamentMatchFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TournamentMatchFiles",
                table: "TournamentMatchFiles");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TournamentMatchComments",
                table: "TournamentMatchComments");

            migrationBuilder.RenameTable(
                name: "TournamentMatchFiles",
                newName: "TournamentMatchFile");

            migrationBuilder.RenameTable(
                name: "TournamentMatchComments",
                newName: "TournamentMatchComment");

            migrationBuilder.RenameIndex(
                name: "IX_TournamentMatchFiles_MatchId",
                table: "TournamentMatchFile",
                newName: "IX_TournamentMatchFile_MatchId");

            migrationBuilder.RenameIndex(
                name: "IX_TournamentMatchComments_MatchId",
                table: "TournamentMatchComment",
                newName: "IX_TournamentMatchComment_MatchId");

            migrationBuilder.RenameIndex(
                name: "IX_TournamentMatchComments_CreatedByUserId",
                table: "TournamentMatchComment",
                newName: "IX_TournamentMatchComment_CreatedByUserId");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TournamentMatchFile",
                table: "TournamentMatchFile",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TournamentMatchComment",
                table: "TournamentMatchComment",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchComment_TournamentMatches_MatchId",
                table: "TournamentMatchComment",
                column: "MatchId",
                principalTable: "TournamentMatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchComment_Users_CreatedByUserId",
                table: "TournamentMatchComment",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatchFile_TournamentMatches_MatchId",
                table: "TournamentMatchFile",
                column: "MatchId",
                principalTable: "TournamentMatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
