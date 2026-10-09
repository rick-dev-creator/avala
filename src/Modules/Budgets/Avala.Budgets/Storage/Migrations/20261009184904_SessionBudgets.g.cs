using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Budgets.Storage.Migrations
{
    internal sealed partial class SessionBudgets : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionBudgets",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Session = table.Column<Guid>(type: "TEXT", nullable: false),
                    Connection = table.Column<string>(type: "TEXT", nullable: false),
                    Budget = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionBudgets", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionBudgets_Session",
                table: "SessionBudgets",
                column: "Session");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SessionBudgets");
        }
    }
}
