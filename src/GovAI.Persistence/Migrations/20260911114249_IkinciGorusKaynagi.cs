using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IkinciGorusKaynagi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_expert_verdicts_assessment_id",
                schema: "govai",
                table: "expert_verdicts");

            migrationBuilder.AddColumn<decimal>(
                name: "ai_confidence",
                schema: "govai",
                table: "expert_verdicts",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reviewer_model",
                schema: "govai",
                table: "expert_verdicts",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "source",
                schema: "govai",
                table: "expert_verdicts",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_expert_verdicts_assessment_id_source",
                schema: "govai",
                table: "expert_verdicts",
                columns: new[] { "assessment_id", "source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_expert_verdicts_assessment_id_source",
                schema: "govai",
                table: "expert_verdicts");

            migrationBuilder.DropColumn(
                name: "ai_confidence",
                schema: "govai",
                table: "expert_verdicts");

            migrationBuilder.DropColumn(
                name: "reviewer_model",
                schema: "govai",
                table: "expert_verdicts");

            migrationBuilder.DropColumn(
                name: "source",
                schema: "govai",
                table: "expert_verdicts");

            migrationBuilder.CreateIndex(
                name: "ix_expert_verdicts_assessment_id",
                schema: "govai",
                table: "expert_verdicts",
                column: "assessment_id",
                unique: true);
        }
    }
}
