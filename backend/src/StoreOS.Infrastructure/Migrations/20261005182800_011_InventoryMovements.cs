using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _011_InventoryMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_movements",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    imei_id = table.Column<Guid>(type: "uuid", nullable: true),
                    movement_type = table.Column<string>(type: "text", nullable: false),
                    qty_delta = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ref_type = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_movements", x => x.id);
                    table.ForeignKey(
                        name: "FK_inventory_movements_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_movements_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "public",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_branch_id",
                schema: "public",
                table: "inventory_movements",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_movements_product_id",
                schema: "public",
                table: "inventory_movements",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_movements_tenant_branch_product",
                schema: "public",
                table: "inventory_movements",
                columns: new[] { "tenant_id", "branch_id", "product_id" });

            migrationBuilder.Sql("ALTER TABLE public.inventory_movements ADD CONSTRAINT ck_inventory_movements_qty_delta CHECK (qty_delta <> 0);");

            migrationBuilder.Sql(@"
                ALTER TABLE public.inventory_movements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.inventory_movements FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_branch_isolation_policy ON public.inventory_movements USING (
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
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.inventory_movements;");

            migrationBuilder.DropTable(
                name: "inventory_movements",
                schema: "public");
        }
    }
}
