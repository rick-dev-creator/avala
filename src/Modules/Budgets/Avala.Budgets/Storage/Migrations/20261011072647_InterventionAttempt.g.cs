using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Budgets.Storage.Migrations
{
    internal sealed partial class InterventionAttempt : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Attempt",
                table: "Interventions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Attempt",
                table: "Interventions");
        }
    }
}
