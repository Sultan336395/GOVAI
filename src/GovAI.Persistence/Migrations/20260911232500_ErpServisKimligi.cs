using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ErpServisKimligi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "erp_assertion_uses",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    token_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_erp_assertion_uses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "erp_service_identities",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    last_token_issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_erp_service_identities", x => x.id);
                    table.ForeignKey(
                        name: "fk_erp_service_identities_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "govai",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "erp_signing_keys",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    erp_service_identity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    algorithm = table.Column<int>(type: "integer", nullable: false),
                    public_key_pem = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_erp_signing_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_erp_signing_keys_erp_service_identities_erp_service_identit",
                        column: x => x.erp_service_identity_id,
                        principalSchema: "govai",
                        principalTable: "erp_service_identities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_erp_assertion_uses_client_id_token_id",
                schema: "govai",
                table: "erp_assertion_uses",
                columns: new[] { "client_id", "token_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_erp_assertion_uses_expires_at",
                schema: "govai",
                table: "erp_assertion_uses",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_erp_service_identities_client_id",
                schema: "govai",
                table: "erp_service_identities",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_erp_service_identities_company_id",
                schema: "govai",
                table: "erp_service_identities",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_erp_signing_keys_erp_service_identity_id_key_id",
                schema: "govai",
                table: "erp_signing_keys",
                columns: new[] { "erp_service_identity_id", "key_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "erp_assertion_uses",
                schema: "govai");

            migrationBuilder.DropTable(
                name: "erp_signing_keys",
                schema: "govai");

            migrationBuilder.DropTable(
                name: "erp_service_identities",
                schema: "govai");
        }
    }
}
