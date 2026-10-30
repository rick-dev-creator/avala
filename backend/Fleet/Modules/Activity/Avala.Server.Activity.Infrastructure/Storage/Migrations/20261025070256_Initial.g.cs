using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Server.Activity.Infrastructure.Storage.Migrations
{
    internal sealed partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "activity");

            migrationBuilder.CreateTable(
                name: "instances",
                schema: "activity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    input_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    output_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    cache_read_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    cache_write_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    reasoning_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instances", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "activity",
                columns: table => new
                {
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repository = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    status_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hold = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    hold_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    effort = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    model_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    last_outcome = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    outcome_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    input_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    output_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    cache_read_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    cache_write_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    reasoning_tokens = table.Column<decimal>(type: "numeric(30,0)", precision: 30, scale: 0, nullable: false),
                    first_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_activity_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_jobs", x => new { x.instance_id, x.job_id });
                });

            migrationBuilder.CreateTable(
                name: "received_events",
                schema: "activity",
                columns: table => new
                {
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_received_events", x => new { x.instance_id, x.event_id });
                });

            migrationBuilder.CreateTable(
                name: "instance_costs",
                schema: "activity",
                columns: table => new
                {
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_instance_costs", x => new { x.instance_id, x.currency, x.basis });
                    table.ForeignKey(
                        name: "FK_instance_costs_instances_instance_id",
                        column: x => x.instance_id,
                        principalSchema: "activity",
                        principalTable: "instances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "job_costs",
                schema: "activity",
                columns: table => new
                {
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    basis = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    instance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(28,6)", precision: 28, scale: 6, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_job_costs", x => new { x.instance_id, x.job_id, x.currency, x.basis });
                    table.ForeignKey(
                        name: "FK_job_costs_jobs_instance_id_job_id",
                        columns: x => new { x.instance_id, x.job_id },
                        principalSchema: "activity",
                        principalTable: "jobs",
                        principalColumns: new[] { "instance_id", "job_id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_jobs_repository",
                schema: "activity",
                table: "jobs",
                column: "repository");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "instance_costs",
                schema: "activity");

            migrationBuilder.DropTable(
                name: "job_costs",
                schema: "activity");

            migrationBuilder.DropTable(
                name: "received_events",
                schema: "activity");

            migrationBuilder.DropTable(
                name: "instances",
                schema: "activity");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "activity");
        }
    }
}
