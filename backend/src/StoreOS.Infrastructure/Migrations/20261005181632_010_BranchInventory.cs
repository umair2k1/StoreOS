using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _010_BranchInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "branch_inventory",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stock_qty = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    cost_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    sell_price_override = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    low_stock_threshold = table.Column<int>(type: "integer", nullable: false, defaultValue: 3)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_branch_inventory", x => x.id);
                    table.ForeignKey(
                        name: "FK_branch_inventory_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_branch_inventory_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "public",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_branch_inventory_branch_id",
                schema: "public",
                table: "branch_inventory",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_branch_inventory_product_id",
                schema: "public",
                table: "branch_inventory",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_branch_inventory_tenant_branch_product",
                schema: "public",
                table: "branch_inventory",
                columns: new[] { "tenant_id", "branch_id", "product_id" },
                unique: true);

            migrationBuilder.Sql("ALTER TABLE public.branch_inventory ADD CONSTRAINT ck_branch_inventory_stock_qty CHECK (stock_qty >= 0);");

            migrationBuilder.Sql(@"
                ALTER TABLE public.branch_inventory ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.branch_inventory FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_branch_isolation_policy ON public.branch_inventory USING (
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
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.branch_inventory;");

            migrationBuilder.DropTable(
                name: "branch_inventory",
                schema: "public");
        }
    }
}
