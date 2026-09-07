using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MevzuatDayanagi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "legal_basis",
                schema: "govai",
                table: "opportunities",
                type: "character varying(400)",
                maxLength: 400,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "legal_basis",
                schema: "govai",
                table: "opportunities");
        }
    }
}
