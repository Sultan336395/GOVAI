using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AnalizCalistirmalari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analysis_runs",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    target_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_profile_version = table.Column<int>(type: "integer", nullable: false),
                    financial_data_version = table.Column<int>(type: "integer", nullable: false),
                    document_versions_csv = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    rule_set_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    prompt_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    prompt_template_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    model_provider = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    model_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    model_parameters = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    output_schema_version = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    ai_status = table.Column<int>(type: "integer", nullable: false),
                    error_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    confidence = table.Column<decimal>(type: "numeric(6,4)", precision: 6, scale: 4, nullable: true),
                    confidence_level = table.Column<int>(type: "integer", nullable: true),
                    verdict = table.Column<int>(type: "integer", nullable: true),
                    impact = table.Column<int>(type: "integer", nullable: true),
                    result_json = table.Column<string>(type: "jsonb", nullable: false),
                    is_latest = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_analysis_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_analysis_runs_company_id_target_id_is_latest",
                schema: "govai",
                table: "analysis_runs",
                columns: new[] { "company_id", "target_id", "is_latest" });

            migrationBuilder.CreateIndex(
                name: "ix_analysis_runs_idempotency_key",
                schema: "govai",
                table: "analysis_runs",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_analysis_runs_target_id_is_latest",
                schema: "govai",
                table: "analysis_runs",
                columns: new[] { "target_id", "is_latest" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analysis_runs",
                schema: "govai");
        }
    }
}
