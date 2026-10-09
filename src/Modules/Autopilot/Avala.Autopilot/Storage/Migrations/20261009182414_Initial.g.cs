using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Autopilot.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tasks",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Repository = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false),
                    TaskKey = table.Column<string>(type: "TEXT", nullable: false),
                    Instruction = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Taken = table.Column<long>(type: "INTEGER", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tasks", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_Repository_Source_TaskKey",
                table: "Tasks",
                columns: new[] { "Repository", "Source", "TaskKey" },
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Tasks");
        }
    }
}
