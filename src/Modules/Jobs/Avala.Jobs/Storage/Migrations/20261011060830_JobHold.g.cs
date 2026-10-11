using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Jobs.Storage.Migrations
{
    internal sealed partial class JobHold : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Hold",
                table: "Jobs",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Hold",
                table: "Jobs");
        }
    }
}
