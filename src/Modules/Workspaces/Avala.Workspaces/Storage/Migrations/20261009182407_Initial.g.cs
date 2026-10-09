using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Workspaces.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Location = table.Column<string>(type: "TEXT", nullable: false),
                    Branch = table.Column<string>(type: "TEXT", nullable: false),
                    Base = table.Column<string>(type: "TEXT", nullable: false),
                    BaseBranch = table.Column<string>(type: "TEXT", nullable: false),
                    Rules = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceCheckpoints",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Commit = table.Column<string>(type: "TEXT", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceCheckpoints", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WorkspaceCheckpoints_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceCheckpoints_WorkspaceId",
                table: "WorkspaceCheckpoints",
                column: "WorkspaceId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkspaceCheckpoints");

            migrationBuilder.DropTable(
                name: "Workspaces");
        }
    }
}
