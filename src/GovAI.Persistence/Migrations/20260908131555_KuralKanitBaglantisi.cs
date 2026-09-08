using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class KuralKanitBaglantisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "opportunity_rule_evidence",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_rule_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_chunk_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    start_offset = table.Column<int>(type: "integer", nullable: false),
                    end_offset = table.Column<int>(type: "integer", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    section_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opportunity_rule_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_opportunity_rule_evidence_document_evidence_chunks_evidence",
                        column: x => x.evidence_chunk_id,
                        principalSchema: "govai",
                        principalTable: "document_evidence_chunks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_opportunity_rule_evidence_opportunity_rule_opportunity_rule",
                        column: x => x.opportunity_rule_id,
                        principalSchema: "govai",
                        principalTable: "opportunity_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_opportunity_rule_evidence_source_document_versions_document",
                        column: x => x.document_version_id,
                        principalSchema: "govai",
                        principalTable: "source_document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_rule_evidence_document_version_id",
                schema: "govai",
                table: "opportunity_rule_evidence",
                column: "document_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_rule_evidence_evidence_chunk_id",
                schema: "govai",
                table: "opportunity_rule_evidence",
                column: "evidence_chunk_id");

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_rule_evidence_opportunity_rule_id_evidence_chun",
                schema: "govai",
                table: "opportunity_rule_evidence",
                columns: new[] { "opportunity_rule_id", "evidence_chunk_id", "role" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opportunity_rule_evidence",
                schema: "govai");
        }
    }
}
