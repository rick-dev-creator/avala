using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Projects.Storage.Migrations
{
    internal sealed partial class ArchivedConversations : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Archived",
                columns: table => new
                {
                    Job = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Archived", x => x.Job);
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Archived");
        }
    }
}
