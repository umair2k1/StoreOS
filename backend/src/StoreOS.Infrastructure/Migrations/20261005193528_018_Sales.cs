using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _018_Sales : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sales",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_number = table.Column<string>(type: "text", nullable: false),
                    invoice_type = table.Column<string>(type: "text", nullable: false),
                    sale_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_name = table.Column<string>(type: "text", nullable: true),
                    customer_gstin = table.Column<string>(type: "text", nullable: true),
                    total_taxable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_cgst = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    total_sgst = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    amount_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "Active"),
                    payment_mode = table.Column<string>(type: "text", nullable: true),
                    finance_provider = table.Column<string>(type: "text", nullable: true),
                    financer_name = table.Column<string>(type: "text", nullable: true),
                    down_payment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    emi_details = table.Column<string>(type: "jsonb", nullable: true),
                    gift_details = table.Column<string>(type: "jsonb", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    repair_job_id = table.Column<Guid>(type: "uuid", nullable: true),
                    irn = table.Column<string>(type: "text", nullable: true),
                    irn_ack_no = table.Column<string>(type: "text", nullable: true),
                    irn_qr_data = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sales", x => x.id);
                    table.ForeignKey(
                        name: "FK_sales_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "sale_items",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    item_name = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    price_per_unit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    purchase_price_snapshot = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    hsn_snapshot = table.Column<string>(type: "text", nullable: true),
                    is_margin_scheme = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_gift = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    taxable = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    cgst = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    sgst = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    imei = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sale_items", x => x.id);
                    table.ForeignKey(
                        name: "FK_sale_items_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "public",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_sale_items_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "public",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_imei",
                schema: "public",
                table: "sale_items",
                columns: new[] { "tenant_id", "imei" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_items_product_id",
                schema: "public",
                table: "sale_items",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_sale_items_sale",
                schema: "public",
                table: "sale_items",
                columns: new[] { "tenant_id", "sale_id" });

            migrationBuilder.CreateIndex(
                name: "IX_sale_items_sale_id",
                schema: "public",
                table: "sale_items",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "IX_sales_branch_id",
                schema: "public",
                table: "sales",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_branch_date",
                schema: "public",
                table: "sales",
                columns: new[] { "tenant_id", "branch_id", "sale_date" });

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_invoice_number",
                schema: "public",
                table: "sales",
                columns: new[] { "tenant_id", "invoice_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_irn",
                schema: "public",
                table: "sales",
                columns: new[] { "tenant_id", "irn" },
                filter: "irn IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_sales_tenant_status",
                schema: "public",
                table: "sales",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.Sql("ALTER TABLE public.sales ADD CONSTRAINT ck_sales_invoice_type CHECK (invoice_type IN ('TaxInvoice','Estimate'));");
            migrationBuilder.Sql("ALTER TABLE public.sales ADD CONSTRAINT ck_sales_status CHECK (status IN ('Active','Voided','Refunded'));");
            migrationBuilder.Sql("ALTER TABLE public.sales ADD CONSTRAINT ck_sales_payment_mode CHECK (payment_mode IS NULL OR payment_mode IN ('Cash','UPI','Card','Credit','Finance'));");
            migrationBuilder.Sql("ALTER TABLE public.sale_items ADD CONSTRAINT ck_sale_items_qty CHECK (qty > 0);");

            migrationBuilder.Sql(@"
                ALTER TABLE public.sales ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.sales FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_branch_isolation_policy ON public.sales USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                  AND (
                    COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
                    OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
                  )
                );

                ALTER TABLE public.sale_items ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.sale_items FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON public.sale_items USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.sales;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.sale_items;");
            migrationBuilder.DropTable(
                name: "sale_items",
                schema: "public");

            migrationBuilder.DropTable(
                name: "sales",
                schema: "public");
        }
    }
}
