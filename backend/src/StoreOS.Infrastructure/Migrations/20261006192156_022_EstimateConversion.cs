using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _022_EstimateConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "converted_from_estimate_id",
                schema: "public",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "linked_tax_invoice_id",
                schema: "public",
                table: "sales",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_sales_converted_from_estimate_id",
                schema: "public",
                table: "sales",
                column: "converted_from_estimate_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_linked_tax_invoice_id",
                schema: "public",
                table: "sales",
                column: "linked_tax_invoice_id");

            migrationBuilder.AddForeignKey(
                name: "FK_sales_sales_converted_from_estimate_id",
                schema: "public",
                table: "sales",
                column: "converted_from_estimate_id",
                principalSchema: "public",
                principalTable: "sales",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_sales_sales_linked_tax_invoice_id",
                schema: "public",
                table: "sales",
                column: "linked_tax_invoice_id",
                principalSchema: "public",
                principalTable: "sales",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_sales_sales_converted_from_estimate_id",
                schema: "public",
                table: "sales");

            migrationBuilder.DropForeignKey(
                name: "FK_sales_sales_linked_tax_invoice_id",
                schema: "public",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_converted_from_estimate_id",
                schema: "public",
                table: "sales");

            migrationBuilder.DropIndex(
                name: "IX_sales_linked_tax_invoice_id",
                schema: "public",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "converted_from_estimate_id",
                schema: "public",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "linked_tax_invoice_id",
                schema: "public",
                table: "sales");
        }
    }
}
