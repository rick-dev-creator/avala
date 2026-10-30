using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Fleet.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Announced",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    Job = table.Column<Guid>(type: "TEXT", nullable: false),
                    Keyless = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Announced", x => new { x.Scope, x.Job });
                });

            migrationBuilder.CreateTable(
                name: "Outbox",
                columns: table => new
                {
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    DueAt = table.Column<long>(type: "INTEGER", nullable: false),
                    DeferredReason = table.Column<string>(type: "TEXT", nullable: false),
                    FirstDeferredAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Outbox", x => x.Sequence);
                });

            migrationBuilder.CreateTable(
                name: "Rejections",
                columns: table => new
                {
                    Key = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rejections", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "Scopes",
                columns: table => new
                {
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    LastDelivery = table.Column<long>(type: "INTEGER", nullable: false),
                    Dropped = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scopes", x => x.Scope);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Id",
                table: "Outbox",
                column: "Id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Outbox_Scope_DueAt",
                table: "Outbox",
                columns: new[] { "Scope", "DueAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Rejections_Scope",
                table: "Rejections",
                column: "Scope");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Announced");

            migrationBuilder.DropTable(
                name: "Outbox");

            migrationBuilder.DropTable(
                name: "Rejections");

            migrationBuilder.DropTable(
                name: "Scopes");
        }
    }
}
