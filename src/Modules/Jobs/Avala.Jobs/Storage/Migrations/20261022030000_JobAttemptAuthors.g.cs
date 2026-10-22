using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Jobs.Storage.Migrations
{
    internal sealed partial class JobAttemptAuthors : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Author",
                table: "JobAttempts",
                type: "TEXT",
                nullable: false,
                defaultValue: "Person");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Author",
                table: "JobAttempts");
        }
    }
}
