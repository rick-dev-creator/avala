using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Documentation.Storage.Migrations
{
    internal sealed partial class VersionAttempts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                table: "Versions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempt",
                table: "Versions");
        }
    }
}
