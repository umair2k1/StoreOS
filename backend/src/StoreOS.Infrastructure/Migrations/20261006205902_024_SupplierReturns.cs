using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _024_SupplierReturns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_returns",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_number = table.Column<string>(type: "text", nullable: false),
                    return_date = table.Column<DateOnly>(type: "date", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_phone = table.Column<string>(type: "text", nullable: true),
                    supplier_address = table.Column<string>(type: "text", nullable: true),
                    total_qty = table.Column<int>(type: "integer", nullable: false),
                    total_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    settlement_type = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_returns", x => x.id);
                    table.CheckConstraint("CK_supplier_returns_settlement_type", "settlement_type IN ('PayableDeduction', 'CashRefund', 'PendingCredit')");
                    table.ForeignKey(
                        name: "FK_supplier_returns_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_returns_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "public",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_return_items",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    return_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_name = table.Column<string>(type: "text", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    purchase_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    return_rate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    imei_numbers = table.Column<string[]>(type: "text[]", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_return_items", x => x.id);
                    table.CheckConstraint("CK_supplier_return_items_qty", "qty > 0");
                    table.ForeignKey(
                        name: "FK_supplier_return_items_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "public",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_return_items_supplier_returns_return_id",
                        column: x => x.return_id,
                        principalSchema: "public",
                        principalTable: "supplier_returns",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_return_items_product_id",
                schema: "public",
                table: "supplier_return_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_return_items_return",
                schema: "public",
                table: "supplier_return_items",
                columns: new[] { "tenant_id", "return_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_return_items_return_id",
                schema: "public",
                table: "supplier_return_items",
                column: "return_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_returns_branch_id",
                schema: "public",
                table: "supplier_returns",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_returns_supplier_id",
                schema: "public",
                table: "supplier_returns",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_supplier_returns_tenant_branch_date",
                schema: "public",
                table: "supplier_returns",
                columns: new[] { "tenant_id", "branch_id", "return_date" });

            migrationBuilder.CreateIndex(
                name: "ix_supplier_returns_tenant_supplier",
                schema: "public",
                table: "supplier_returns",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "uq_supplier_returns_tenant_return_number",
                schema: "public",
                table: "supplier_returns",
                columns: new[] { "tenant_id", "return_number" },
                unique: true);

            migrationBuilder.Sql(@"
                ALTER TABLE public.supplier_returns ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.supplier_returns FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_branch_isolation_policy ON public.supplier_returns USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                  AND (
                    COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
                    OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
                  )
                );

                ALTER TABLE public.supplier_return_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.supplier_return_items FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON public.supplier_return_items USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.supplier_returns;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.supplier_return_items;");
            migrationBuilder.DropTable(
                name: "supplier_return_items",
                schema: "public");

            migrationBuilder.DropTable(
                name: "supplier_returns",
                schema: "public");
        }
    }
}
