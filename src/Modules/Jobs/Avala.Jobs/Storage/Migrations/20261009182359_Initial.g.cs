using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Jobs.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Instruction = table.Column<string>(type: "TEXT", nullable: false),
                    Budget = table.Column<int>(type: "INTEGER", nullable: false),
                    Repository = table.Column<string>(type: "TEXT", nullable: false),
                    Submitted = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    Workspace = table.Column<Guid>(type: "TEXT", nullable: false),
                    Session = table.Column<Guid>(type: "TEXT", nullable: false),
                    Resume = table.Column<string>(type: "TEXT", nullable: false),
                    Autonomy = table.Column<string>(type: "TEXT", nullable: false),
                    Connection = table.Column<string>(type: "TEXT", nullable: false),
                    Parent = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobAttempts",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Origin = table.Column<string>(type: "TEXT", nullable: false),
                    Guidance = table.Column<string>(type: "TEXT", nullable: false),
                    Session = table.Column<Guid>(type: "TEXT", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobAttempts", x => x.Key);
                    table.ForeignKey(
                        name: "FK_JobAttempts_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobAttempts_JobId",
                table: "JobAttempts",
                column: "JobId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobAttempts");

            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
