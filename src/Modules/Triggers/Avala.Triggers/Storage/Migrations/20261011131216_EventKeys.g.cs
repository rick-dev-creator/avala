using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Triggers.Storage.Migrations
{
    internal sealed partial class EventKeys : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EventKeys",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventKeys", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EventKeys_Value",
                table: "EventKeys",
                column: "Value",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EventKeys");
        }
    }
}
