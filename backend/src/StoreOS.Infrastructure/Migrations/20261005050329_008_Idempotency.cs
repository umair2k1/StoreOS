using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _008_Idempotency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "public",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    key = table.Column<string>(type: "text", nullable: false),
                    endpoint = table.Column<string>(type: "text", nullable: false),
                    request_hash = table.Column<string>(type: "text", nullable: false),
                    response_status = table.Column<int>(type: "integer", nullable: true),
                    response_body = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => new { x.tenant_id, x.user_id, x.endpoint, x.key });
                });

            migrationBuilder.CreateIndex(
                name: "ix_idempotency_created",
                schema: "public",
                table: "idempotency_keys",
                column: "created_at");

            migrationBuilder.Sql(@"
                ALTER TABLE public.idempotency_keys ENABLE ROW LEVEL SECURITY;
                ALTER TABLE public.idempotency_keys FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_isolation_policy ON public.idempotency_keys
                USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation_policy ON public.idempotency_keys;");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "public");
        }
    }
}
