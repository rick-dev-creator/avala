using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Jobs.Storage.Migrations
{
    internal sealed partial class ConnectionChoices : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConnectionChoices",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Choice = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionChoices", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConnectionChoices_Job",
                table: "ConnectionChoices",
                column: "Job");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConnectionChoices");
        }
    }
}
