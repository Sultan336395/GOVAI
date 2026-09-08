using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class YillikMaliVeri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "financial_data_version",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "company_annual_financials",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fiscal_year = table.Column<int>(type: "integer", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    annual_revenue = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    annual_income = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    annual_expense = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    net_profit_or_loss = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    balance_total = table.Column<decimal>(type: "numeric(20,2)", precision: 20, scale: 2, nullable: true),
                    data_source = table.Column<int>(type: "integer", nullable: false),
                    verification_status = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_annual_financials", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_annual_financials_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "govai",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_company_annual_financials_company_id_fiscal_year",
                schema: "govai",
                table: "company_annual_financials",
                columns: new[] { "company_id", "fiscal_year" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_annual_financials",
                schema: "govai");

            migrationBuilder.DropColumn(
                name: "financial_data_version",
                schema: "govai",
                table: "companies");
        }
    }
}
