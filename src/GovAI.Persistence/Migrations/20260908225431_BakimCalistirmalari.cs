using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BakimCalistirmalari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "maintenance_runs",
                schema: "govai",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<int>(type: "integer", nullable: false),
                    plan_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    detail_json = table.Column<string>(type: "jsonb", nullable: false),
                    changed_count = table.Column<int>(type: "integer", nullable: false),
                    performed_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    undone_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    undone_by = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_maintenance_runs", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_runs_operation_started_at",
                schema: "govai",
                table: "maintenance_runs",
                columns: new[] { "operation", "started_at" });

            migrationBuilder.CreateIndex(
                name: "ix_maintenance_runs_started_at",
                schema: "govai",
                table: "maintenance_runs",
                column: "started_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "maintenance_runs",
                schema: "govai");
        }
    }
}
