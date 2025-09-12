using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class AddServerOnlineStatusFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOnline",
                table: "Servers",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastSeenTime",
                table: "Servers",
                type: "TEXT",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOnline",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "LastSeenTime",
                table: "Servers");
        }
    }
}
