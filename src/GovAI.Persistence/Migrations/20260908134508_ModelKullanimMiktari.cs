using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ModelKullanimMiktari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "completion_tokens",
                schema: "govai",
                table: "analysis_runs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "prompt_tokens",
                schema: "govai",
                table: "analysis_runs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "completion_tokens",
                schema: "govai",
                table: "analysis_runs");

            migrationBuilder.DropColumn(
                name: "prompt_tokens",
                schema: "govai",
                table: "analysis_runs");
        }
    }
}
