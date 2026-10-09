using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Delegation.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Records",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Session = table.Column<Guid>(type: "TEXT", nullable: false),
                    Item = table.Column<string>(type: "TEXT", nullable: false),
                    Parent = table.Column<Guid>(type: "TEXT", nullable: false),
                    Child = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Record = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Records", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Records_Child",
                table: "Records",
                column: "Child");

            migrationBuilder.CreateIndex(
                name: "IX_Records_Parent",
                table: "Records",
                column: "Parent");

            migrationBuilder.CreateIndex(
                name: "IX_Records_Session_Item",
                table: "Records",
                columns: new[] { "Session", "Item" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Records");
        }
    }
}
