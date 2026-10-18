using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Documentation.Storage.Migrations
{
    internal sealed partial class JobPlans : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Plan",
                table: "Choices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Plan",
                table: "Choices");
        }
    }
}
