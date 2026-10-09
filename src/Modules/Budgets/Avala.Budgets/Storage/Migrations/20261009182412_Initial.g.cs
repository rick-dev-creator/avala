using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Budgets.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Carves",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Parent = table.Column<Guid>(type: "TEXT", nullable: false),
                    Child = table.Column<Guid>(type: "TEXT", nullable: false),
                    Costs = table.Column<string>(type: "TEXT", nullable: false),
                    Tokens = table.Column<long>(type: "INTEGER", nullable: false),
                    Share = table.Column<double>(type: "REAL", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carves", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Interventions",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Session = table.Column<Guid>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    Halt = table.Column<string>(type: "TEXT", nullable: false),
                    Measure = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    Measured = table.Column<decimal>(type: "TEXT", nullable: false),
                    Cap = table.Column<decimal>(type: "TEXT", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Interventions", x => x.Key);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Carves");

            migrationBuilder.DropTable(
                name: "Interventions");
        }
    }
}
