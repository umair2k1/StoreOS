using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _015_ImportBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "import_batches",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    source_type = table.Column<string>(type: "text", nullable: false),
                    source_ref = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    record_counts = table.Column<string>(type: "jsonb", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_import_batches", x => x.id);
                    table.ForeignKey(
                        name: "FK_import_batches_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "public",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_import_batches_branch_id",
                schema: "public",
                table: "import_batches",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_import_batches_tenant_source_ref",
                schema: "public",
                table: "import_batches",
                columns: new[] { "tenant_id", "source_ref" });

            migrationBuilder.Sql(@"
                ALTER TABLE public.import_batches ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.import_batches FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON public.import_batches
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.import_batches;");

            migrationBuilder.DropTable(
                name: "import_batches",
                schema: "public");
        }
    }
}
