using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _009_Products : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "products",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    sell_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    purchase_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    gst_rate = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false, defaultValue: 18m),
                    is_taxable = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_margin_scheme = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    hsn_code = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    estimated_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    warranty_days = table.Column<int>(type: "integer", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_products", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_cat",
                schema: "public",
                table: "products",
                columns: new[] { "tenant_id", "category" });

            migrationBuilder.CreateIndex(
                name: "ix_products_tenant_name",
                schema: "public",
                table: "products",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.Sql("ALTER TABLE public.products ADD CONSTRAINT ck_products_category CHECK (category IN ('NewPhone','UsedPhone','Accessory','RepairService'));");

            migrationBuilder.Sql(@"
                ALTER TABLE public.products ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.products FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON public.products
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.products;");

            migrationBuilder.DropTable(
                name: "products",
                schema: "public");
        }
    }
}
