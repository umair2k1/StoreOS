using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _031_MigrationSupportColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "original_invoice_number",
                schema: "public",
                table: "sales",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "desktop_id",
                schema: "public",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_desktop_id",
                schema: "public",
                table: "customers",
                columns: new[] { "tenant_id", "desktop_id" },
                filter: "desktop_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_customers_tenant_desktop_id",
                schema: "public",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "original_invoice_number",
                schema: "public",
                table: "sales");

            migrationBuilder.DropColumn(
                name: "desktop_id",
                schema: "public",
                table: "customers");
        }
    }
}
