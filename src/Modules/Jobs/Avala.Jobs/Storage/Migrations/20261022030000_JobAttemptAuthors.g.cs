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

            migrationBuilder.AddColumn<string>(
                name: "Cause",
                table: "JobAttempts",
                type: "TEXT",
                nullable: false,
                defaultValue: "Person");

            migrationBuilder.Sql("UPDATE \"JobAttempts\" SET \"Author\" = 'Avala', \"Cause\" = 'Avala' WHERE \"Origin\" IN ('Retry', 'Recovery', 'Woken', 'Reopen', 'Handoff');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Author",
                table: "JobAttempts");

            migrationBuilder.DropColumn(
                name: "Cause",
                table: "JobAttempts");
        }
    }
}
