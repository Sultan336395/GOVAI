using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiCompanyFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address",
                schema: "govai",
                table: "companies",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                schema: "govai",
                table: "companies",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "corporate_email",
                schema: "govai",
                table: "companies",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "country",
                schema: "govai",
                table: "companies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                schema: "govai",
                table: "companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                schema: "govai",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "is_head_company",
                schema: "govai",
                table: "companies",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "main_sector",
                schema: "govai",
                table: "companies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mersis_number",
                schema: "govai",
                table: "companies",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "parent_company_id",
                schema: "govai",
                table: "companies",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                schema: "govai",
                table: "companies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "profile_completion_percentage",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "relationship_type",
                schema: "govai",
                table: "companies",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "short_name",
                schema: "govai",
                table: "companies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sub_sectors_json",
                schema: "govai",
                table: "companies",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "target_countries_json",
                schema: "govai",
                table: "companies",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "tax_office",
                schema: "govai",
                table: "companies",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "trade_registry_number",
                schema: "govai",
                table: "companies",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "website",
                schema: "govai",
                table: "companies",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "company_groups",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_groups", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "company_invitations",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    company_role = table.Column<int>(type: "integer", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    invited_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_company_invitations_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "govai",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_verification_requests",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tax_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_legal_name = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_company_verification_requests", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_companies",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_role = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_companies_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "govai",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_companies_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "govai",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_companies_group_id",
                schema: "govai",
                table: "companies",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_companies_parent_company_id",
                schema: "govai",
                table: "companies",
                column: "parent_company_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_groups_tenant_id",
                schema: "govai",
                table: "company_groups",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_groups_tenant_id_name",
                schema: "govai",
                table: "company_groups",
                columns: new[] { "tenant_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_invitations_company_id",
                schema: "govai",
                table: "company_invitations",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_company_invitations_tenant_id_company_id",
                schema: "govai",
                table: "company_invitations",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_company_invitations_token_hash",
                schema: "govai",
                table: "company_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_company_verification_requests_status",
                schema: "govai",
                table: "company_verification_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_company_verification_requests_tenant_id_tax_number",
                schema: "govai",
                table: "company_verification_requests",
                columns: new[] { "tenant_id", "tax_number" });

            migrationBuilder.CreateIndex(
                name: "ix_user_companies_company_id",
                schema: "govai",
                table: "user_companies",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_companies_single_default",
                schema: "govai",
                table: "user_companies",
                column: "user_id",
                unique: true,
                filter: "is_default = true");

            migrationBuilder.CreateIndex(
                name: "ix_user_companies_tenant_id_company_id",
                schema: "govai",
                table: "user_companies",
                columns: new[] { "tenant_id", "company_id" });

            migrationBuilder.CreateIndex(
                name: "ix_user_companies_user_id_company_id",
                schema: "govai",
                table: "user_companies",
                columns: new[] { "user_id", "company_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_companies_companies_parent_company_id",
                schema: "govai",
                table: "companies",
                column: "parent_company_id",
                principalSchema: "govai",
                principalTable: "companies",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_companies_company_groups_group_id",
                schema: "govai",
                table: "companies",
                column: "group_id",
                principalSchema: "govai",
                principalTable: "company_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            // ──────────────────────────────────────────────────────────────────
            //  VERİ TAŞIMA
            //
            //  Mevcut kullanıcılara UserCompany üyeliği üretilir. Şirket erişimi bu
            //  fazdan sonra user_companies tablosunu esas alır; users.scoped_company_ids_json
            //  yalnızca geçici geri dönüş uyumluluğu için yerinde bırakılır (SİLİNMEZ).
            //
            //  Tekrar çalıştırılabilir: her ekleme NOT EXISTS ile korunur, bu yüzden
            //  migration yeniden uygulanırsa mükerrer üyelik oluşmaz.
            //
            //  Rol eşlemesi (users.role -> user_companies.company_role):
            //    SuperAdmin(1)     -> CompanyOwner(1)
            //    CompanyManager(2) -> CompanyManager(2)
            //    OperationUser(3)  -> CompanyExpert(3)
            //    Consultant(4)     -> CompanyExpert(3)
            //    ReadOnly(5)       -> CompanyViewer(4)
            //  Platform rolleri (10,11,12) şirket üyeliği almaz: bunlar kiracı
            //  kullanıcısı değildir.
            // ──────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                -- 1) Kapsamı TANIMLI kullanıcılar: yalnızca listedeki şirketlere üyelik.
                INSERT INTO govai.user_companies
                    (id, tenant_id, user_id, company_id, company_role, is_default, is_active,
                     created_at, created_by, is_deleted)
                SELECT
                    gen_random_uuid(),
                    u.tenant_id,
                    u.id,
                    c.id,
                    CASE u.role WHEN 1 THEN 1 WHEN 2 THEN 2 WHEN 3 THEN 3 WHEN 4 THEN 3 ELSE 4 END,
                    false,
                    u.is_active,
                    now(),
                    'migration:MultiCompanyFoundation',
                    false
                FROM govai.users u
                JOIN LATERAL jsonb_array_elements_text(u.scoped_company_ids_json) AS scoped(company_id) ON true
                JOIN govai.companies c
                     ON c.id = scoped.company_id::uuid
                    AND c.tenant_id = u.tenant_id
                WHERE u.is_deleted = false
                  AND u.role < 10
                  AND u.scoped_company_ids_json IS NOT NULL
                  AND jsonb_typeof(u.scoped_company_ids_json) = 'array'
                  AND jsonb_array_length(u.scoped_company_ids_json) > 0
                  AND NOT EXISTS (
                      SELECT 1 FROM govai.user_companies uc
                      WHERE uc.user_id = u.id AND uc.company_id = c.id);

                -- 2) Kapsamı BOŞ kullanıcılar: kendi kiracısındaki tüm şirketlere üyelik.
                INSERT INTO govai.user_companies
                    (id, tenant_id, user_id, company_id, company_role, is_default, is_active,
                     created_at, created_by, is_deleted)
                SELECT
                    gen_random_uuid(),
                    u.tenant_id,
                    u.id,
                    c.id,
                    CASE u.role WHEN 1 THEN 1 WHEN 2 THEN 2 WHEN 3 THEN 3 WHEN 4 THEN 3 ELSE 4 END,
                    false,
                    u.is_active,
                    now(),
                    'migration:MultiCompanyFoundation',
                    false
                FROM govai.users u
                JOIN govai.companies c ON c.tenant_id = u.tenant_id AND c.is_deleted = false
                WHERE u.is_deleted = false
                  AND u.role < 10
                  AND (u.scoped_company_ids_json IS NULL
                       OR jsonb_typeof(u.scoped_company_ids_json) <> 'array'
                       OR jsonb_array_length(u.scoped_company_ids_json) = 0)
                  AND NOT EXISTS (
                      SELECT 1 FROM govai.user_companies uc
                      WHERE uc.user_id = u.id AND uc.company_id = c.id);

                -- 3) Her kullanıcı için tek bir varsayılan şirket: en eski üyelik seçilir.
                --    Halihazırda varsayılanı olan kullanıcıya dokunulmaz (tekrar çalıştırma güvenliği).
                WITH ilk_uyelik AS (
                    SELECT DISTINCT ON (uc.user_id) uc.id
                    FROM govai.user_companies uc
                    WHERE uc.is_active = true
                      AND uc.is_deleted = false
                      AND NOT EXISTS (
                          SELECT 1 FROM govai.user_companies d
                          WHERE d.user_id = uc.user_id AND d.is_default = true)
                    ORDER BY uc.user_id, uc.created_at, uc.id
                )
                UPDATE govai.user_companies
                SET is_default = true
                WHERE id IN (SELECT id FROM ilk_uyelik);

                -- 4) Şirket alanlarının güvenli varsayılanları.
                UPDATE govai.companies
                SET relationship_type = 0
                WHERE relationship_type IS NULL;

                UPDATE govai.companies
                SET is_active = true
                WHERE is_active IS NULL;
                """);

            // ──────────────────────────────────────────────────────────────────
            //  companies.tenant_id için yabancı anahtar
            //
            //  İlk şemada bu sütun vardı ama kısıt yoktu: var olmayan bir kiracıya
            //  şirket bağlanmasını engelleyen hiçbir şey bulunmuyordu.
            //
            //  Öksüz kayıt varsa migration BİLEREK durur. Sessizce atlamak, kısıt
            //  eklenmiş sanılan ama aslında korumasız bir şema bırakırdı; sessizce
            //  silmek ise veri kaybı olurdu. Operatör önce veriyi düzeltmelidir.
            // ──────────────────────────────────────────────────────────────────
            migrationBuilder.Sql("""
                DO $$
                DECLARE oksuz integer;
                BEGIN
                    SELECT count(*) INTO oksuz
                    FROM govai.companies c
                    WHERE NOT EXISTS (SELECT 1 FROM govai.tenants t WHERE t.id = c.tenant_id);

                    IF oksuz > 0 THEN
                        RAISE EXCEPTION
                            'companies.tenant_id icin yabanci anahtar eklenemedi: % adet sirket var olmayan bir kiraciya bagli. Once bu kayitlar duzeltilmelidir.',
                            oksuz;
                    END IF;

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

            // Veri taşıma geri alınırken üretilen üyelikler kaldırılır; elle oluşturulmuş
            // üyelikler korunur (created_by damgasına göre ayrılır).
            migrationBuilder.Sql("""
                ALTER TABLE govai.companies DROP CONSTRAINT IF EXISTS fk_companies_tenants_tenant_id;
                DELETE FROM govai.user_companies WHERE created_by = 'migration:MultiCompanyFoundation';
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_companies_companies_parent_company_id",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropForeignKey(
                name: "fk_companies_company_groups_group_id",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropTable(
                name: "company_groups",
                schema: "govai");

            migrationBuilder.DropTable(
                name: "company_invitations",
                schema: "govai");

            migrationBuilder.DropTable(
                name: "company_verification_requests",
                schema: "govai");

            migrationBuilder.DropTable(
                name: "user_companies",
                schema: "govai");

            migrationBuilder.DropIndex(
                name: "ix_companies_group_id",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropIndex(
                name: "ix_companies_parent_company_id",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "address",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "city",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "corporate_email",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "country",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "group_id",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "is_active",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "is_head_company",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "main_sector",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "mersis_number",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "parent_company_id",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "phone",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "profile_completion_percentage",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "relationship_type",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "short_name",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "sub_sectors_json",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "target_countries_json",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "tax_office",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "trade_registry_number",
                schema: "govai",
                table: "companies");

            migrationBuilder.DropColumn(
                name: "website",
                schema: "govai",
                table: "companies");
        }
    }
}
