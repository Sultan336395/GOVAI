using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PlatformActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "platform_activations",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    role = table.Column<int>(type: "integer", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_activations", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_activations_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "govai",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_activations_token_hash",
                schema: "govai",
                table: "platform_activations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_platform_activations_user_id",
                schema: "govai",
                table: "platform_activations",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_activations",
                schema: "govai");
        }
    }
}
