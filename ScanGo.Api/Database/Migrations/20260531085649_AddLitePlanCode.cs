using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScanGo.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddLitePlanCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_plan",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_plan",
                table: "users",
                sql: "plan IN ('free', 'lite', 'basic_monthly', 'pro_monthly', 'pro_yearly', 'unlimited')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_users_plan",
                table: "users");

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_plan",
                table: "users",
                sql: "plan IN ('free', 'basic_monthly', 'pro_monthly', 'pro_yearly', 'unlimited')");
        }
    }
}
