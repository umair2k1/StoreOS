using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _028_RepairStatusHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "repair_job_status_history",
                schema: "public",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    repair_job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "text", nullable: true),
                    to_status = table.Column<string>(type: "text", nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_repair_job_status_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_repair_job_status_history_repair_jobs_repair_job_id",
                        column: x => x.repair_job_id,
                        principalSchema: "public",
                        principalTable: "repair_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_repair_job_status_history_repair_job_id",
                schema: "public",
                table: "repair_job_status_history",
                column: "repair_job_id");

            migrationBuilder.CreateIndex(
                name: "ix_repair_job_status_history_tenant_job",
                schema: "public",
                table: "repair_job_status_history",
                columns: new[] { "tenant_id", "repair_job_id" });

            migrationBuilder.Sql(@"
                ALTER TABLE public.repair_job_status_history ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.repair_job_status_history FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON public.repair_job_status_history
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.repair_job_status_history;");
            migrationBuilder.DropTable(
                name: "repair_job_status_history",
                schema: "public");
        }
    }
}
