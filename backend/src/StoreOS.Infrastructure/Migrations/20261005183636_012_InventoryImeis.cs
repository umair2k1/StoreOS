using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _012_InventoryImeis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_imeis",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_id = table.Column<Guid>(type: "uuid", nullable: true),
                    imei = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    sale_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    return_id = table.Column<Guid>(type: "uuid", nullable: true),
                    purchase_date = table.Column<DateOnly>(type: "date", nullable: true),
                    purchase_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_imeis", x => x.id);
                    table.ForeignKey(
                        name: "FK_inventory_imeis_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_imeis_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "public",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_imei_lookup",
                schema: "public",
                table: "inventory_imeis",
                columns: new[] { "tenant_id", "imei" });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_imeis_branch_id",
                schema: "public",
                table: "inventory_imeis",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_imeis_product_id",
                schema: "public",
                table: "inventory_imeis",
                column: "product_id");

            migrationBuilder.Sql("ALTER TABLE public.inventory_imeis ADD CONSTRAINT ck_inventory_imeis_status CHECK (status IN ('InStock','Sold','ReturnedToSupplier','InTransit'));");

            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_imei_active ON public.inventory_imeis (tenant_id, imei) WHERE status IN ('InStock','Sold','InTransit');");

            migrationBuilder.Sql(@"
                ALTER TABLE public.inventory_imeis ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.inventory_imeis FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_branch_isolation_policy ON public.inventory_imeis USING (
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
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.inventory_imeis;");
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_imei_active;");

            migrationBuilder.DropTable(
                name: "inventory_imeis",
                schema: "public");
        }
    }
}
