using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Verification.Storage.Migrations
{
    internal sealed partial class ChecksDiscoveries : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Discoveries",
                columns: table => new
                {
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Repository = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Scout = table.Column<Guid>(type: "TEXT", nullable: false),
                    Follows = table.Column<Guid>(type: "TEXT", nullable: false),
                    Declaration = table.Column<string>(type: "TEXT", nullable: false),
                    Proposal = table.Column<string>(type: "TEXT", nullable: false),
                    Reads = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ran = table.Column<string>(type: "TEXT", nullable: false),
                    Final = table.Column<bool>(type: "INTEGER", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Discoveries", x => x.Job);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Discoveries");
        }
    }
}
