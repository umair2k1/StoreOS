using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _023_SupplierTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "supplier_ledger_entries",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    entry_type = table.Column<string>(type: "text", nullable: false),
                    debit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    credit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, defaultValue: 0m),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    ref_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_ledger_entries_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_ledger_entries_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "public",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "supplier_transactions",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_mode = table.Column<string>(type: "text", nullable: true),
                    reference_no = table.Column<string>(type: "text", nullable: true),
                    txn_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_transactions", x => x.id);
                    table.CheckConstraint("CK_supplier_transactions_amount", "amount > 0");
                    table.CheckConstraint("CK_supplier_transactions_payment_mode", "payment_mode IS NULL OR payment_mode IN ('Cash', 'UPI', 'BankTransfer', 'Cheque', 'Credit')");
                    table.CheckConstraint("CK_supplier_transactions_type", "type IN ('Bill', 'Payment', 'OpeningBalance')");
                    table.ForeignKey(
                        name: "FK_supplier_transactions_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_supplier_transactions_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "public",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_suppledger_supplier",
                schema: "public",
                table: "supplier_ledger_entries",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_ledger_entries_branch_id",
                schema: "public",
                table: "supplier_ledger_entries",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_ledger_entries_supplier_id",
                schema: "public",
                table: "supplier_ledger_entries",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_transactions_branch_id",
                schema: "public",
                table: "supplier_transactions",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_transactions_supplier_id",
                schema: "public",
                table: "supplier_transactions",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_suppliertxn_supplier",
                schema: "public",
                table: "supplier_transactions",
                columns: new[] { "tenant_id", "supplier_id" });

            migrationBuilder.Sql(@"
                ALTER TABLE public.supplier_transactions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.supplier_transactions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON public.supplier_transactions USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );

                ALTER TABLE public.supplier_ledger_entries ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.supplier_ledger_entries FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON public.supplier_ledger_entries USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.supplier_transactions;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.supplier_ledger_entries;");
            migrationBuilder.DropTable(
                name: "supplier_ledger_entries",
                schema: "public");

            migrationBuilder.DropTable(
                name: "supplier_transactions",
                schema: "public");
        }
    }
}
