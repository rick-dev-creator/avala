using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Observability.Storage.Migrations
{
    internal sealed partial class SessionOpened : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Opened",
                table: "UsageSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Opened",
                table: "UsageSessions");
        }
    }
}
