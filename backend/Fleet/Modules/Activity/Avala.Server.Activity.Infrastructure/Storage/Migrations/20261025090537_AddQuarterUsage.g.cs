using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Server.Activity.Infrastructure.Storage.Migrations
{
    internal sealed partial class AddQuarterUsage : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "quarter_usage",
                schema: "activity",
                columns: table => new
                {
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quarter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    input_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    output_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    cache_read_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    cache_write_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    reasoning_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quarter_usage", x => new { x.instance_id, x.job_id, x.quarter, x.model });
                });

            migrationBuilder.CreateTable(
                name: "quarter_costs",
                schema: "activity",
                columns: table => new
                {
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quarter = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_quarter_costs", x => new { x.instance_id, x.job_id, x.quarter, x.model, x.currency, x.basis });
                    table.ForeignKey(
                        name: "FK_quarter_costs_quarter_usage_instance_id_job_id_quarter_model",
                        columns: x => new { x.instance_id, x.job_id, x.quarter, x.model },
                        principalSchema: "activity",
                        principalTable: "quarter_usage",
                        principalColumns: new[] { "instance_id", "job_id", "quarter", "model" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_quarter_usage_quarter",
                schema: "activity",
                table: "quarter_usage",
                column: "quarter");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "quarter_costs",
                schema: "activity");

            migrationBuilder.DropTable(
                name: "quarter_usage",
                schema: "activity");
        }
    }
}
