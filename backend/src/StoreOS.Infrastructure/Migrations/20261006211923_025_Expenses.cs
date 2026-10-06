using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _025_Expenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expenses",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    description = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    expense_date = table.Column<DateOnly>(type: "date", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_expenses", x => x.id);
                    table.CheckConstraint("CK_expenses_amount", "amount > 0");
                    table.ForeignKey(
                        name: "FK_expenses_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_expenses_branch_id",
                schema: "public",
                table: "expenses",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_tenant_branch_date",
                schema: "public",
                table: "expenses",
                columns: new[] { "tenant_id", "branch_id", "expense_date" });

            migrationBuilder.Sql(@"
                ALTER TABLE public.expenses ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.expenses FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_branch_isolation_policy ON public.expenses
                USING (
                    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                    AND (
                        COALESCE(branch_id, '00000000-0000-0000-0000-000000000000'::uuid) = '00000000-0000-0000-0000-000000000000'::uuid
                        OR COALESCE(NULLIF(current_setting('app.all_branches', true), ''), 'false') = 'true'
                        OR branch_id = ANY (string_to_array(NULLIF(current_setting('app.branch_ids', true), ''), ',')::uuid[])
                    )
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.expenses;");
            migrationBuilder.DropTable(
                name: "expenses",
                schema: "public");
        }
    }
}
