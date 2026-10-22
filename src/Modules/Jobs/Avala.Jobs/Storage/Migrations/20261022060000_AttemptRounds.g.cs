using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Avala.Jobs.Storage.Migrations
{
    internal sealed partial class AttemptRounds : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ContinuesRound",
                table: "JobAttempts",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.Sql("UPDATE JobAttempts SET ContinuesRound = 1 WHERE Origin = 'Handoff' AND EXISTS (SELECT 1 FROM JobAttempts AS previous WHERE previous.JobId = JobAttempts.JobId AND previous.Number = JobAttempts.Number - 1 AND previous.Outcome = 'Rejected')");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContinuesRound",
                table: "JobAttempts");
        }
    }
}
