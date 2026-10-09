using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Observability.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "UsageFacts",
                columns: table => new
                {
                    Key = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Session = table.Column<Guid>(type: "TEXT", nullable: false),
                    At = table.Column<long>(type: "INTEGER", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    Input = table.Column<long>(type: "INTEGER", nullable: false),
                    Output = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheRead = table.Column<long>(type: "INTEGER", nullable: false),
                    CacheWrite = table.Column<long>(type: "INTEGER", nullable: false),
                    Reasoning = table.Column<long>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", nullable: false),
                    Window = table.Column<string>(type: "TEXT", nullable: false),
                    UsedFraction = table.Column<double>(type: "REAL", nullable: false),
                    ResetsAt = table.Column<long>(type: "INTEGER", nullable: false),
                    Outcome = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageFacts", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "UsageSessions",
                columns: table => new
                {
                    Session = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderName = table.Column<string>(type: "TEXT", nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", nullable: false),
                    AccountLabel = table.Column<string>(type: "TEXT", nullable: false),
                    Connection = table.Column<string>(type: "TEXT", nullable: false),
                    Job = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageSessions", x => x.Session);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UsageFacts_At",
                table: "UsageFacts",
                column: "At");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UsageFacts");

            migrationBuilder.DropTable(
                name: "UsageSessions");
        }
    }
}
