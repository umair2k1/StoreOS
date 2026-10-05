using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _016_Transfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "inventory_transfers",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    qty = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    imei_ids = table.Column<Guid[]>(type: "uuid[]", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_inventory_transfers", x => x.id);
                    table.ForeignKey(
                        name: "FK_inventory_transfers_branches_from_branch_id",
                        column: x => x.from_branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transfers_branches_to_branch_id",
                        column: x => x.to_branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_inventory_transfers_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "public",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transfers_from_branch_id",
                schema: "public",
                table: "inventory_transfers",
                column: "from_branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transfers_product_id",
                schema: "public",
                table: "inventory_transfers",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "IX_inventory_transfers_to_branch_id",
                schema: "public",
                table: "inventory_transfers",
                column: "to_branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfers_tenant_from_branch",
                schema: "public",
                table: "inventory_transfers",
                columns: new[] { "tenant_id", "from_branch_id" });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_tenant_to_branch",
                schema: "public",
                table: "inventory_transfers",
                columns: new[] { "tenant_id", "to_branch_id" });

            migrationBuilder.Sql("ALTER TABLE public.inventory_transfers ADD CONSTRAINT ck_inventory_transfers_qty CHECK (qty > 0);");
            migrationBuilder.Sql("ALTER TABLE public.inventory_transfers ADD CONSTRAINT ck_inventory_transfers_status CHECK (status IN ('Pending','Received'));");

            migrationBuilder.Sql(@"
                ALTER TABLE public.inventory_transfers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.inventory_transfers FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_branch_dual_isolation_policy ON public.inventory_transfers USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                  AND (
                    COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
                    OR from_branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
                    OR to_branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
                  )
                );
            ");

            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.inventory_imeis;
                CREATE POLICY tenant_branch_isolation_policy ON public.inventory_imeis USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                  AND (
                    COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
                    OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
                    OR status = 'InTransit'
                  )
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_dual_isolation_policy ON public.inventory_transfers;");
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.inventory_imeis;
                CREATE POLICY tenant_branch_isolation_policy ON public.inventory_imeis USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                  AND (
                    COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
                    OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
                  )
                );
            ");

            migrationBuilder.DropTable(
                name: "inventory_transfers",
                schema: "public");
        }
    }
}
