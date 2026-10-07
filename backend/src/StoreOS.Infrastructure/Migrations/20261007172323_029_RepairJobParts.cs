using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _029_RepairJobParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repair_job_parts",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repair_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: true),
                    part_name = table.Column<string>(type: "text", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    charge_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_job_parts", x => x.id);
                    table.CheckConstraint("CK_repair_job_parts_quantity", "quantity > 0");
                    table.CheckConstraint("CK_repair_job_parts_status", "status IN ('Reserved','Consumed','Returned')");
                    table.ForeignKey(
                        name: "FK_repair_job_parts_repair_jobs_repair_job_id",
                        column: x => x.repair_job_id,
                        principalSchema: "public",
                        principalTable: "repair_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_repair_job_parts_repair_job_id",
                schema: "public",
                table: "repair_job_parts",
                column: "repair_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_repair_job_parts_tenant_job",
                schema: "public",
                table: "repair_job_parts",
                columns: new[] { "tenant_id", "repair_job_id" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_job_parts_tenant_product",
                schema: "public",
                table: "repair_job_parts",
                columns: new[] { "tenant_id", "product_id" });

            migrationBuilder.Sql(@"
                ALTER TABLE public.repair_job_parts ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.repair_job_parts FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON public.repair_job_parts
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.repair_job_parts;");
            migrationBuilder.DropTable(
                name: "repair_job_parts",
                schema: "public");
        }
    }
}
