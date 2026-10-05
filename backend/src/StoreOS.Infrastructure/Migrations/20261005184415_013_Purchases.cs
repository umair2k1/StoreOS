using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _013_Purchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "purchases",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    purchase_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: true),
                    supplier_name = table.Column<string>(type: "text", nullable: true),
                    supplier_phone = table.Column<string>(type: "text", nullable: true),
                    supplier_address = table.Column<string>(type: "text", nullable: true),
                    seller_name = table.Column<string>(type: "text", nullable: true),
                    seller_phone = table.Column<string>(type: "text", nullable: true),
                    seller_address = table.Column<string>(type: "text", nullable: true),
                    supplier_invoice_no = table.Column<string>(type: "text", nullable: true),
                    import_batch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_purchases", x => x.id);
                    table.ForeignKey(
                        name: "FK_purchases_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_purchases_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "public",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_purchases_branch_id",
                schema: "public",
                table: "purchases",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_purchases_product_id",
                schema: "public",
                table: "purchases",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_purchases_tenant_branch_date",
                schema: "public",
                table: "purchases",
                columns: new[] { "tenant_id", "branch_id", "purchase_date" });

            migrationBuilder.CreateIndex(
                name: "ix_purchases_tenant_branch_product",
                schema: "public",
                table: "purchases",
                columns: new[] { "tenant_id", "branch_id", "product_id" });

            migrationBuilder.Sql("ALTER TABLE public.purchases ADD CONSTRAINT ck_purchases_qty CHECK (qty > 0);");

            migrationBuilder.Sql(@"
                ALTER TABLE public.purchases ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.purchases FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_branch_isolation_policy ON public.purchases USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                  AND (
                    COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
                    OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
                  )
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.purchases;");

            migrationBuilder.DropTable(
                name: "purchases",
                schema: "public");
        }
    }
}
