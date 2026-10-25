using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Resources.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RestingJobs",
                columns: table => new
                {
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Since = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RestingJobs", x => x.Job);
                });

            migrationBuilder.CreateTable(
                name: "SeenWorktrees",
                columns: table => new
                {
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    First = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeenWorktrees", x => x.Path);
                });

            migrationBuilder.CreateTable(
                name: "WorktreeDays",
                columns: table => new
                {
                    Day = table.Column<string>(type: "TEXT", nullable: false),
                    Peak = table.Column<long>(type: "INTEGER", nullable: false),
                    Worktrees = table.Column<int>(type: "INTEGER", nullable: false),
                    Created = table.Column<int>(type: "INTEGER", nullable: false),
                    Freed = table.Column<int>(type: "INTEGER", nullable: false),
                    Recovered = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorktreeDays", x => x.Day);
                });

            migrationBuilder.CreateTable(
                name: "WorktreeEvents",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Path = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Moment = table.Column<string>(type: "TEXT", nullable: false),
                    Bytes = table.Column<long>(type: "INTEGER", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Lifetime = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorktreeEvents", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorktreeEvents_At",
                table: "WorktreeEvents",
                column: "At");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RestingJobs");

            migrationBuilder.DropTable(
                name: "SeenWorktrees");

            migrationBuilder.DropTable(
                name: "WorktreeDays");

            migrationBuilder.DropTable(
                name: "WorktreeEvents");
        }
    }
}
