using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _019_CustomersLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customers",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    gstin = table.Column<string>(type: "text", nullable: true),
                    total_purchases = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "customer_ledger_entries",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_type = table.Column<string>(type: "text", nullable: false),
                    debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    credit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_ledger_entries_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_ledger_entries_customers_customer_id",
                        column: x => x.customer_id,
                        principalSchema: "public",
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_customer_ledger_entries_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "public",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_custledger_customer",
                schema: "public",
                table: "customer_ledger_entries",
                columns: new[] { "tenant_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "IX_customer_ledger_entries_branch_id",
                schema: "public",
                table: "customer_ledger_entries",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_ledger_entries_customer_id",
                schema: "public",
                table: "customer_ledger_entries",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "IX_customer_ledger_entries_sale_id",
                schema: "public",
                table: "customer_ledger_entries",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_customers_tenant_phone",
                schema: "public",
                table: "customers",
                columns: new[] { "tenant_id", "phone" });

            migrationBuilder.Sql(@"
                ALTER TABLE public.customers ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.customers FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON public.customers USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );

                ALTER TABLE public.customer_ledger_entries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.customer_ledger_entries FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON public.customer_ledger_entries USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.customers;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.customer_ledger_entries;");
            migrationBuilder.DropTable(
                name: "customer_ledger_entries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "customers",
                schema: "public");
        }
    }
}
