using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _006_RlsBranchScopedPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE rls_probe_branch_scoped (
                    id uuid PRIMARY KEY,
                    tenant_id uuid NOT NULL,
                    branch_id uuid NOT NULL,
                    label text NOT NULL
                );

                ALTER TABLE rls_probe_branch_scoped ENABLE ROW LEVEL SECURITY;
                ALTER TABLE rls_probe_branch_scoped FORCE ROW LEVEL SECURITY;

                CREATE POLICY tenant_branch_isolation_policy ON rls_probe_branch_scoped USING (
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
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_branch_isolation_policy ON rls_probe_branch_scoped;
                DROP TABLE IF EXISTS rls_probe_branch_scoped;
            ");
        }
    }
}
