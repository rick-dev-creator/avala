using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Handoffs.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Handoffs",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Handoff = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Handoffs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Waits",
                columns: table => new
                {
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Pending = table.Column<string>(type: "TEXT", nullable: false),
                    Since = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Waits", x => x.Job);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Handoffs_Job",
                table: "Handoffs",
                column: "Job");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Handoffs");

            migrationBuilder.DropTable(
                name: "Waits");
        }
    }
}
