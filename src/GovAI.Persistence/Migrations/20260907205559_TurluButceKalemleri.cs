using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TurluButceKalemleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "opportunity_budget_items",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    excerpt = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    start_offset = table.Column<int>(type: "integer", nullable: false),
                    end_offset = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opportunity_budget_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_opportunity_budget_items_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalSchema: "govai",
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "opportunity_budget_rates",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    opportunity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    rate = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    excerpt = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: true),
                    start_offset = table.Column<int>(type: "integer", nullable: false),
                    end_offset = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_opportunity_budget_rates", x => x.id);
                    table.ForeignKey(
                        name: "fk_opportunity_budget_rates_opportunities_opportunity_id",
                        column: x => x.opportunity_id,
                        principalSchema: "govai",
                        principalTable: "opportunities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_budget_items_opportunity_id_type",
                schema: "govai",
                table: "opportunity_budget_items",
                columns: new[] { "opportunity_id", "type" });

            migrationBuilder.CreateIndex(
                name: "ix_opportunity_budget_rates_opportunity_id_type",
                schema: "govai",
                table: "opportunity_budget_rates",
                columns: new[] { "opportunity_id", "type" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "opportunity_budget_items",
                schema: "govai");

            migrationBuilder.DropTable(
                name: "opportunity_budget_rates",
                schema: "govai");
        }
    }
}
