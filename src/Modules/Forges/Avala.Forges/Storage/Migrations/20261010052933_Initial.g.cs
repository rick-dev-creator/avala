using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Forges.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WakeUps",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    WakeUp = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WakeUps", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Watches",
                columns: table => new
                {
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Watch = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Watches", x => x.Job);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WakeUps_Job",
                table: "WakeUps",
                column: "Job");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WakeUps");

            migrationBuilder.DropTable(
                name: "Watches");
        }
    }
}
