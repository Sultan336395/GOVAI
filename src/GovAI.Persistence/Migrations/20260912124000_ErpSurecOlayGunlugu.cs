using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErpSurecOlayGunlugu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "erp_process_events",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    activity = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resource = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    department = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_erp_process_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_erp_process_events_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "govai",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_erp_process_events_company_id_case_id_activity_occurred_at",
                schema: "govai",
                table: "erp_process_events",
                columns: new[] { "company_id", "case_id", "activity", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_erp_process_events_company_id_external_id",
                schema: "govai",
                table: "erp_process_events",
                columns: new[] { "company_id", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_erp_process_events_tenant_id_company_id_occurred_at",
                schema: "govai",
                table: "erp_process_events",
                columns: new[] { "tenant_id", "company_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "erp_process_events",
                schema: "govai");
        }
    }
}
