using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScanGo.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddRolling7PeriodKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_usage_summary_period_kind",
                table: "usage_summaries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_usage_summary_period_kind",
                table: "usage_summaries",
                sql: "period_kind IN ('weekly', 'monthly', 'rolling_7d')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_usage_summary_period_kind",
                table: "usage_summaries");

            migrationBuilder.AddCheckConstraint(
                name: "ck_usage_summary_period_kind",
                table: "usage_summaries",
                sql: "period_kind IN ('weekly', 'monthly')");
        }
    }
}
