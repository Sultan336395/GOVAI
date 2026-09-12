using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PersonelKirilimiAcikBeyan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "young_employee_count",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "women_employee_count",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "rnd_employee_count",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<int>(
                name: "disabled_employee_count",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // MEVCUT SIFIRLAR BİLİNMEYENE ÇEVRİLİR.
            //
            // Bu kolonlar bugüne kadar NOT NULL'dı: girilmemiş alan 0 olarak duruyordu
            // ve motor onu "gerçekten sıfır" sayıyordu. Dönüşüm yapılmazsa değişiklik
            // mevcut veride HİÇBİR İŞE YARAMAZ — pilot firma "kadın çalışanı yok" diye
            // kayıtlı kalır ve elenmeye devam eder.
            //
            // Yön güvenli taraftan seçildi: bugün 0 ile "girilmedi" ayrılamıyor,
            // dolayısıyla her 0 belirsizdir. Belirsizi "bilinmiyor" saymak kararı
            // ASKIYA ALIR; "sıfır" saymak firmayı ELER. Ürünün üçüncü iddiası
            // ("eksik veri firmayı elemez") ilkini gerektirir.
            //
            // Gerçekten sıfır beyan etmek isteyen firma bunu artık açıkça yazabilir:
            // alan boş bırakılabilir olduğu için 0 yazmak bir beyandır.
            migrationBuilder.Sql(
                """
                UPDATE govai.companies
                SET women_employee_count    = NULLIF(women_employee_count, 0),
                    young_employee_count    = NULLIF(young_employee_count, 0),
                    rnd_employee_count      = NULLIF(rnd_employee_count, 0),
                    disabled_employee_count = NULLIF(disabled_employee_count, 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "young_employee_count",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "women_employee_count",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "rnd_employee_count",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "disabled_employee_count",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
