using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Budgets.Storage.Migrations
{
    internal sealed partial class Raises : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Raises",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Raise = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Raises", x => x.Key);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Raises");
        }
    }
}
