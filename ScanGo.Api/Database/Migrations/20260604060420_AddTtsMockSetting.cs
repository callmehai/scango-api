using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScanGo.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddTtsMockSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "tts_mock",
                table: "app_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "tts_mock",
                table: "app_settings");
        }
    }
}
