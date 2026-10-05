using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _014_SuppliersMaster : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    brand_or_category = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_suppliers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_tenant_name",
                schema: "public",
                table: "suppliers",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.Sql(@"
                ALTER TABLE public.suppliers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.suppliers FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON public.suppliers
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE public.purchases ADD CONSTRAINT fk_purchases_supplier
                    FOREIGN KEY (supplier_id) REFERENCES public.suppliers(id);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE public.purchases DROP CONSTRAINT IF EXISTS fk_purchases_supplier;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.suppliers;");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "public");
        }
    }
}
