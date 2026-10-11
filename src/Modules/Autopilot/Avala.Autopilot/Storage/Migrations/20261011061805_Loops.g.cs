using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Autopilot.Storage.Migrations
{
    internal sealed partial class Loops : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Loops",
                columns: table => new
                {
                    Loop = table.Column<Guid>(type: "TEXT", nullable: false),
                    Repository = table.Column<string>(type: "TEXT", nullable: false),
                    Started = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Fact = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Loops", x => x.Loop);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Loops_Status_Started",
                table: "Loops",
                columns: new[] { "Status", "Started" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Loops");
        }
    }
}
