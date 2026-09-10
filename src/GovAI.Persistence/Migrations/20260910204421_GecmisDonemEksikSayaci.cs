using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GovAI.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GecmisDonemEksikSayaci : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "past_gap_count",
                schema: "govai",
                table: "weekly_reports",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "past_gap_count",
                schema: "govai",
                table: "weekly_reports");
        }
    }
}
