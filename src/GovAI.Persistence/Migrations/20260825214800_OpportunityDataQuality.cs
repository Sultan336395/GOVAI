using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OpportunityDataQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "availability_budget",
                schema: "govai",
                table: "opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "availability_currency",
                schema: "govai",
                table: "opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "availability_deadline",
                schema: "govai",
                table: "opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "availability_eligible_applicant",
                schema: "govai",
                table: "opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "availability_geography",
                schema: "govai",
                table: "opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "availability_official_document_url",
                schema: "govai",
                table: "opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "availability_programme_type",
                schema: "govai",
                table: "opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "availability_sector",
                schema: "govai",
                table: "opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "quarantine_note",
                schema: "govai",
                table: "opportunities",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "quarantine_reason",
                schema: "govai",
                table: "opportunities",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "ix_opportunities_quarantine",
                schema: "govai",
                table: "opportunities",
                column: "quarantine_reason");

            // ──────────────────────────────────────────────────────────────────
            //  Mevcut kayıtların alan durumu GERÇEK VERİDEN türetilir.
            //
            //  Kolon varsayılanı 0 (Provided) olduğu için hiçbir şey yapılmazsa eski
            //  kayıtlar "bütün alanları resmî kaynaktan çıkarılmış" gibi görünürdü.
            //  Oysa bu alanlar Faz 2'den önce hiç toplanmıyordu.
            //
            //  Bilinebilenler veriye bakılarak, bilinemeyenler NotProvided (1) yazılır.
            //  Tahmin yapılmaz.
            // ──────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                UPDATE govai.opportunities SET
                    availability_deadline = CASE WHEN deadline IS NULL THEN 1 ELSE 0 END,
                    availability_budget   = CASE WHEN budget_min IS NULL AND budget_max IS NULL THEN 1 ELSE 0 END,
                    availability_currency = CASE
                        WHEN budget_currency IS NULL OR btrim(budget_currency) = '' THEN 1 ELSE 0 END,
                    -- Bu dört alan Faz 2'den önce hiç toplanmadı.
                    availability_eligible_applicant = 1,
                    availability_geography          = 1,
                    availability_sector             = 1,
                    availability_programme_type     = 1,
                    -- Resmî belge bağlantısı ayrı bir alandır; source_url onun yerine geçmez.
                    availability_official_document_url = 1;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_opportunities_quarantine",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "availability_budget",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "availability_currency",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "availability_deadline",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "availability_eligible_applicant",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "availability_geography",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "availability_official_document_url",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "availability_programme_type",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "availability_sector",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "quarantine_note",
                schema: "govai",
                table: "opportunities");

            migrationBuilder.DropColumn(
                name: "quarantine_reason",
                schema: "govai",
                table: "opportunities");
        }
    }
}
