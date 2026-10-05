using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _020_CustomerPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "customer_payments",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    payment_mode = table.Column<string>(type: "text", nullable: false),
                    payment_date = table.Column<DateOnly>(type: "date", nullable: false),
                    received_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_customer_payments", x => x.id);
                    table.ForeignKey(
                        name: "FK_customer_payments_sales_sale_id",
                        column: x => x.sale_id,
                        principalSchema: "public",
                        principalTable: "sales",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_customer_payments_sale_id",
                schema: "public",
                table: "customer_payments",
                column: "sale_id");

            migrationBuilder.CreateIndex(
                name: "ix_custpay_sale",
                schema: "public",
                table: "customer_payments",
                columns: new[] { "tenant_id", "sale_id" });

            migrationBuilder.Sql("ALTER TABLE public.customer_payments ADD CONSTRAINT ck_customer_payments_amount CHECK (amount > 0);");

            migrationBuilder.Sql(@"
                ALTER TABLE public.customer_payments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.customer_payments FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON public.customer_payments USING (
                  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.customer_payments;");
            migrationBuilder.DropTable(
                name: "customer_payments",
                schema: "public");
        }
    }
}
