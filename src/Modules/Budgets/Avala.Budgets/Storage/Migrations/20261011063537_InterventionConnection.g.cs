using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Budgets.Storage.Migrations
{
    internal sealed partial class InterventionConnection : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Connection",
                table: "Interventions",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Connection",
                table: "Interventions");
        }
    }
}
