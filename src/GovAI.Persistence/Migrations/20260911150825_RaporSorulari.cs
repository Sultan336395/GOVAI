using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RaporSorulari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "report_inquiries",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    weekly_report_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    question_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    question_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    answer_text = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parent_inquiry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    asked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    asked_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_report_inquiries", x => x.id);
                    table.ForeignKey(
                        name: "fk_report_inquiries_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "govai",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_report_inquiries_weekly_reports_weekly_report_id",
                        column: x => x.weekly_report_id,
                        principalSchema: "govai",
                        principalTable: "weekly_reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_report_inquiries_company_id",
                schema: "govai",
                table: "report_inquiries",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_report_inquiries_weekly_report_id_question_key",
                schema: "govai",
                table: "report_inquiries",
                columns: new[] { "weekly_report_id", "question_key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "report_inquiries",
                schema: "govai");
        }
    }
}
