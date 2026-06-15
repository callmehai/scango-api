using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScanGo.Api.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentNoteRefundedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "payment_orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "refunded_at",
                table: "payment_orders",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "note",
                table: "payment_orders");

            migrationBuilder.DropColumn(
                name: "refunded_at",
                table: "payment_orders");
        }
    }
}
