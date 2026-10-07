using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _027_RepairJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repair_jobs",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_number = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    customer_name = table.Column<string>(type: "text", nullable: true),
                    customer_phone = table.Column<string>(type: "text", nullable: true),
                    customer_address = table.Column<string>(type: "text", nullable: true),
                    device_brand = table.Column<string>(type: "text", nullable: true),
                    device_model = table.Column<string>(type: "text", nullable: true),
                    device_type = table.Column<string>(type: "text", nullable: true),
                    serial_number = table.Column<string>(type: "text", nullable: true),
                    imei1 = table.Column<string>(type: "text", nullable: true),
                    imei2 = table.Column<string>(type: "text", nullable: true),
                    device_condition = table.Column<string>(type: "text", nullable: true),
                    accessories_received = table.Column<string>(type: "text", nullable: true),
                    problem_description = table.Column<string>(type: "text", nullable: true),
                    customer_notes = table.Column<string>(type: "text", nullable: true),
                    diagnosis = table.Column<string>(type: "text", nullable: true),
                    technician_notes = table.Column<string>(type: "text", nullable: true),
                    internal_notes = table.Column<string>(type: "text", nullable: true),
                    technician = table.Column<string>(type: "text", nullable: true),
                    estimated_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    approved_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    parts_cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    labor_charge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    tax = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    final_total = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    advance_paid = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    balance_due = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    expected_completion = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    warranty_until = table.Column<DateOnly>(type: "date", nullable: true),
                    approval_state = table.Column<string>(type: "text", nullable: true),
                    approval_timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "text", nullable: true),
                    invoice_number = table.Column<string>(type: "text", nullable: true),
                    sale_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_jobs", x => x.id);
                    table.CheckConstraint("CK_repair_jobs_status", "status IN ('Received','Diagnosing','WaitingForApproval','WaitingForParts','InRepair','ReadyForPickup','Delivered','Cancelled')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_repair_jobs_tenant_branch_customer",
                schema: "public",
                table: "repair_jobs",
                columns: new[] { "tenant_id", "branch_id", "customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_jobs_tenant_branch_status",
                schema: "public",
                table: "repair_jobs",
                columns: new[] { "tenant_id", "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_repair_jobs_tenant_job_number",
                schema: "public",
                table: "repair_jobs",
                columns: new[] { "tenant_id", "job_number" },
                unique: true);

            migrationBuilder.Sql(@"
                ALTER TABLE public.repair_jobs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.repair_jobs FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_branch_isolation_policy ON public.repair_jobs
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
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_branch_isolation_policy ON public.repair_jobs;");
            migrationBuilder.DropTable(
                name: "repair_jobs",
                schema: "public");
        }
    }
}
