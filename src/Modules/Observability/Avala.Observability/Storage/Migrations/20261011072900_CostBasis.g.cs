using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Observability.Storage.Migrations
{
    internal sealed partial class CostBasis : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Basis",
                table: "UsageFacts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Basis",
                table: "UsageFacts");
        }
    }
}
