using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SirketKiraciIliskisi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kısıt veritabanında ZATEN VAR: MultiCompanyFoundation onu ham SQL ile
            // eklemişti, ama EF modeli ilişkiyi bilmiyordu. Bu migration modeli
            // gerçekle hizalar; şemayı değiştirmez.
            //
            // Bu yüzden koşullu yazılır: mevcut veritabanlarında kısıt zaten
            // durduğundan düz bir AddForeignKey "already exists" ile patlardı ve
            // AutoMigrate açık olan ortamlarda API açılışta durur.
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM pg_constraint
                        WHERE conname = 'fk_companies_tenants_tenant_id'
                    ) THEN
                        ALTER TABLE govai.companies
                            ADD CONSTRAINT fk_companies_tenants_tenant_id
                            FOREIGN KEY (tenant_id) REFERENCES govai.tenants (id) ON DELETE CASCADE;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alma: kısıt MultiCompanyFoundation'a aittir, orada düşürülür.
            migrationBuilder.DropForeignKey(
                name: "fk_companies_tenants_tenant_id",
                schema: "govai",
                table: "companies");
        }
    }
}
