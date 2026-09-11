using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IhaleTakibi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "tender_pursuits",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    owner = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_pursuits", x => x.id);
                    table.ForeignKey(
                        name: "fk_tender_pursuits_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "govai",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_tender_pursuits_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalSchema: "govai",
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tender_pursuit_events",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tender_pursuit_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<int>(type: "integer", nullable: true),
                    to_status = table.Column<int>(type: "integer", nullable: false),
                    outcome = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tender_pursuit_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_tender_pursuit_events_tender_pursuits_tender_pursuit_id",
                        column: x => x.tender_pursuit_id,
                        principalSchema: "govai",
                        principalTable: "tender_pursuits",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_tender_pursuit_events_tender_pursuit_id_at",
                schema: "govai",
                table: "tender_pursuit_events",
                columns: new[] { "tender_pursuit_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_tender_pursuits_company_id_opportunity_id",
                schema: "govai",
                table: "tender_pursuits",
                columns: new[] { "company_id", "opportunity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tender_pursuits_company_id_status",
                schema: "govai",
                table: "tender_pursuits",
                columns: new[] { "company_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_tender_pursuits_opportunity_id",
                schema: "govai",
                table: "tender_pursuits",
                column: "opportunity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "tender_pursuit_events",
                schema: "govai");

            migrationBuilder.DropTable(
                name: "tender_pursuits",
                schema: "govai");
        }
    }
}
