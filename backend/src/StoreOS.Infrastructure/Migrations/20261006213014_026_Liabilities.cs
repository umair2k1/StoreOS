using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _026_Liabilities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "liabilities",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    lender_name = table.Column<string>(type: "text", nullable: true),
                    total_principal = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_liabilities", x => x.id);
                    table.CheckConstraint("CK_liabilities_type", "type IN ('Loan', 'CreditCard', 'Financing', 'Investment')");
                });

            migrationBuilder.CreateTable(
                name: "liability_payments",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    liability_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_mode = table.Column<string>(type: "text", nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_liability_payments", x => x.id);
                    table.CheckConstraint("CK_liability_payments_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_liability_payments_liabilities_liability_id",
                        column: x => x.liability_id,
                        principalSchema: "public",
                        principalTable: "liabilities",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_liabilities_tenant_status",
                schema: "public",
                table: "liabilities",
                columns: new[] { "tenant_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_liability_payments_liability",
                schema: "public",
                table: "liability_payments",
                columns: new[] { "tenant_id", "liability_id" });

            migrationBuilder.CreateIndex(
                name: "IX_liability_payments_liability_id",
                schema: "public",
                table: "liability_payments",
                column: "liability_id");

            migrationBuilder.Sql(@"
                ALTER TABLE public.liabilities ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.liabilities FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON public.liabilities
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);

                ALTER TABLE public.liability_payments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.liability_payments FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON public.liability_payments
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ");
        }
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.liabilities;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.liability_payments;");
            migrationBuilder.DropTable(
                name: "liability_payments",
                schema: "public");

            migrationBuilder.DropTable(
                name: "liabilities",
                schema: "public");
        }
    }
}
