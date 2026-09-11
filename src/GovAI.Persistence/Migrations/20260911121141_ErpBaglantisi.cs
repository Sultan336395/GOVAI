using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErpBaglantisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "erp_connections",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor = table.Column<int>(type: "integer", nullable: false),
                    base_url = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    auth_mode = table.Column<int>(type: "integer", nullable: false),
                    protected_secret = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    is_on_premise = table.Column<bool>(type: "boolean", nullable: false),
                    field_map_json = table.Column<string>(type: "jsonb", nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_run_status = table.Column<int>(type: "integer", nullable: false),
                    last_run_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    consecutive_failure_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_erp_connections", x => x.id);
                    table.ForeignKey(
                        name: "fk_erp_connections_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "govai",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_erp_connections_company_id",
                schema: "govai",
                table: "erp_connections",
                column: "company_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "erp_connections",
                schema: "govai");
        }
    }
}
