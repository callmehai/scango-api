using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScanGo.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddSearchGroundingSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "search_grounding",
                table: "app_settings",
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "search_grounding",
                table: "app_settings");
        }
    }
}
