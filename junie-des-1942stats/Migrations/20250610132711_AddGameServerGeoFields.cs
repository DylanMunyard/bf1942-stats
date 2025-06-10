using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace junie_des_1942stats.Migrations
{
    /// <inheritdoc />
    public partial class AddGameServerGeoFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Country",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GeoLookupDate",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Loc",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Org",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Postal",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Timezone",
                table: "Servers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerObservations_Timestamp",
                table: "PlayerObservations",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PlayerObservations_Timestamp",
                table: "PlayerObservations");

            migrationBuilder.DropColumn(
                name: "City",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "Country",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "GeoLookupDate",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "Loc",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "Org",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "Postal",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "Servers");

            migrationBuilder.DropColumn(
                name: "Timezone",
                table: "Servers");
        }
    }
}
