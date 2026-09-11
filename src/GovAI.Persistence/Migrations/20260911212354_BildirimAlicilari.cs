using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BildirimAlicilari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "delivery_status",
                schema: "govai",
                table: "notifications",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Mevcut kayıtların durumu geriye dönük yazılır. Yazılmasaydı gönderilmiş
            // 53 bildirim "Pending" görünür ve "neden gitmemiş" diye aranırdı.
            // Koşul yalnızca sent_at'e bakar: gönderilmiş olmanın tek kanıtı odur.
            migrationBuilder.Sql(
                """
                UPDATE govai.notifications
                SET delivery_status = 1
                WHERE sent_at IS NOT NULL;
                """);

            migrationBuilder.CreateTable(
                name: "notification_recipients",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    role = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source = table.Column<int>(type: "integer", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_recipients", x => x.id);
                    table.ForeignKey(
                        name: "fk_notification_recipients_companies_company_id",
                        column: x => x.company_id,
                        principalSchema: "govai",
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notification_recipients_company_id_email",
                schema: "govai",
                table: "notification_recipients",
                columns: new[] { "company_id", "email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_recipients_tenant_id_company_id_is_active",
                schema: "govai",
                table: "notification_recipients",
                columns: new[] { "tenant_id", "company_id", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notification_recipients",
                schema: "govai");

            migrationBuilder.DropColumn(
                name: "delivery_status",
                schema: "govai",
                table: "notifications");
        }
    }
}
