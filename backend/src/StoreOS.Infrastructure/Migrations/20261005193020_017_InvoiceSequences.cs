using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _017_InvoiceSequences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invoice_sequences",
                schema: "public",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    series_key = table.Column<string>(type: "text", nullable: false),
                    last_seq = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invoice_sequences", x => new { x.tenant_id, x.branch_id, x.series_key });
                    table.ForeignKey(
                        name: "FK_invoice_sequences_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invoice_sequences_branch_id",
                schema: "public",
                table: "invoice_sequences",
                column: "branch_id");

            migrationBuilder.Sql(@"
                ALTER TABLE public.invoice_sequences ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.invoice_sequences FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_branch_isolation_policy ON public.invoice_sequences USING (
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
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.invoice_sequences;");
            migrationBuilder.DropTable(
                name: "invoice_sequences",
                schema: "public");
        }
    }
}
