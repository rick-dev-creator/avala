using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Jobs.Storage.Migrations
{
    internal sealed partial class JobEnded : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Ended",
                table: "Jobs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Ended",
                table: "Jobs");
        }
    }
}
