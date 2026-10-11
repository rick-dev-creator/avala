using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Triggers.Storage.Migrations
{
    internal sealed partial class QueuedTasks : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueuedTasks",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Run = table.Column<Guid>(type: "TEXT", nullable: false),
                    Fact = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueuedTasks", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueuedTasks_Run",
                table: "QueuedTasks",
                column: "Run",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueuedTasks");
        }
    }
}
