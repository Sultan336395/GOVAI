using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SektorUyumuSiralamasi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "sector_fit",
                schema: "govai",
                table: "eligibility_assessments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Mevcut değerlendirmeler sektör kontrolü YAPILMADAN üretildi. Kolonun sayısal
            // varsayılanı 0'dır ve 0 "Matched" demektir; olduğu gibi bırakılırsa geçmişteki
            // her kayıt "sektör uyumlu" sayılır ve listenin başına çıkar. Bu yüzden tümü
            // açıkça 1 (Unverified) yapılır: doğrulanmadı, uydurulmadı. Kayıtlar bir sonraki
            // yeniden skorlamada gerçek değerini alır.
            migrationBuilder.Sql(
                """UPDATE govai.eligibility_assessments SET sector_fit = 1;""");

            migrationBuilder.CreateIndex(
                name: "ix_eligibility_assessments_company_id_is_latest_sector_fit_fin",
                schema: "govai",
                table: "eligibility_assessments",
                columns: new[] { "company_id", "is_latest", "sector_fit", "final_score" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_eligibility_assessments_company_id_is_latest_sector_fit_fin",
                schema: "govai",
                table: "eligibility_assessments");

            migrationBuilder.DropColumn(
                name: "sector_fit",
                schema: "govai",
                table: "eligibility_assessments");
        }
    }
}
