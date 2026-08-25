using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RegTechSourcePipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "allowed_domains",
                schema: "govai",
                table: "sources",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "authority",
                schema: "govai",
                table: "sources",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "category",
                schema: "govai",
                table: "sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "configuration_verified",
                schema: "govai",
                table: "sources",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "configuration_verified_at",
                schema: "govai",
                table: "sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_selector",
                schema: "govai",
                table: "sources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_types",
                schema: "govai",
                table: "sources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "health",
                schema: "govai",
                table: "sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "jurisdiction",
                schema: "govai",
                table: "sources",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                schema: "govai",
                table: "sources",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_successful_run_at",
                schema: "govai",
                table: "sources",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "list_selector",
                schema: "govai",
                table: "sources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_pages",
                schema: "govai",
                table: "sources",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "official_domain",
                schema: "govai",
                table: "sources",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "start_url",
                schema: "govai",
                table: "sources",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "url_pattern",
                schema: "govai",
                table: "sources",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "canonical_url",
                schema: "govai",
                table: "source_documents",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "quarantine_note",
                schema: "govai",
                table: "source_documents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quarantine_reason",
                schema: "govai",
                table: "source_documents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "regulatory_changes",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    jurisdiction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    authority = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    regulation_domain = table.Column<int>(type: "integer", nullable: false),
                    change_type = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    official_number = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    publication_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    effective_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    summary = table.Column<string>(type: "text", nullable: true),
                    official_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    previous_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    quarantine_reason = table.Column<int>(type: "integer", nullable: false),
                    quarantine_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_regulatory_changes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "source_document_versions",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_number = table.Column<int>(type: "integer", nullable: false),
                    source_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    canonical_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    retrieved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    http_status_code = table.Column<int>(type: "integer", nullable: false),
                    media_type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    charset = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    raw_content = table.Column<string>(type: "text", nullable: false),
                    raw_content_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normalized_text = table.Column<string>(type: "text", nullable: true),
                    normalized_text_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    title = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_modified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    language = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    page_count = table.Column<int>(type: "integer", nullable: true),
                    parse_status = table.Column<int>(type: "integer", nullable: false),
                    requires_ocr = table.Column<bool>(type: "boolean", nullable: false),
                    parse_error = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_document_versions", x => x.id);
                    table.ForeignKey(
                        name: "fk_source_document_versions_source_documents_source_document_id",
                        column: x => x.source_document_id,
                        principalSchema: "govai",
                        principalTable: "source_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "document_evidence_chunks",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence_number = table.Column<int>(type: "integer", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: true),
                    section_title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    paragraph_number = table.Column<int>(type: "integer", nullable: true),
                    text = table.Column<string>(type: "text", nullable: false),
                    text_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    start_offset = table.Column<int>(type: "integer", nullable: false),
                    end_offset = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_document_evidence_chunks", x => x.id);
                    table.ForeignKey(
                        name: "fk_document_evidence_chunks_source_document_versions_document_",
                        column: x => x.document_version_id,
                        principalSchema: "govai",
                        principalTable: "source_document_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sources_category_health",
                schema: "govai",
                table: "sources",
                columns: new[] { "category", "health" });

            migrationBuilder.CreateIndex(
                name: "ix_source_documents_quarantine",
                schema: "govai",
                table: "source_documents",
                column: "quarantine_reason");

            migrationBuilder.CreateIndex(
                name: "ix_document_evidence_chunks_sequence",
                schema: "govai",
                table: "document_evidence_chunks",
                columns: new[] { "document_version_id", "sequence_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_regulatory_changes_lookup",
                schema: "govai",
                table: "regulatory_changes",
                columns: new[] { "jurisdiction", "regulation_domain", "publication_date" });

            migrationBuilder.CreateIndex(
                name: "ix_regulatory_changes_status",
                schema: "govai",
                table: "regulatory_changes",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_regulatory_changes_version_content",
                schema: "govai",
                table: "regulatory_changes",
                columns: new[] { "document_version_id", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_source_document_versions_content_hash",
                schema: "govai",
                table: "source_document_versions",
                columns: new[] { "source_document_id", "raw_content_hash" });

            migrationBuilder.CreateIndex(
                name: "ix_source_document_versions_document_version",
                schema: "govai",
                table: "source_document_versions",
                columns: new[] { "source_document_id", "version_number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_evidence_chunks",
                schema: "govai");

            migrationBuilder.DropTable(
                name: "regulatory_changes",
                schema: "govai");

            migrationBuilder.DropTable(
                name: "source_document_versions",
                schema: "govai");

            migrationBuilder.DropIndex(
                name: "ix_sources_category_health",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropIndex(
                name: "ix_source_documents_quarantine",
                schema: "govai",
                table: "source_documents");

            migrationBuilder.DropColumn(
                name: "allowed_domains",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "authority",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "category",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "configuration_verified",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "configuration_verified_at",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "content_selector",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "document_types",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "health",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "jurisdiction",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "language",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "last_successful_run_at",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "list_selector",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "max_pages",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "official_domain",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "start_url",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "url_pattern",
                schema: "govai",
                table: "sources");

            migrationBuilder.DropColumn(
                name: "canonical_url",
                schema: "govai",
                table: "source_documents");

            migrationBuilder.DropColumn(
                name: "quarantine_note",
                schema: "govai",
                table: "source_documents");

            migrationBuilder.DropColumn(
                name: "quarantine_reason",
                schema: "govai",
                table: "source_documents");
        }
    }
}
