using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StoreOS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class _005_RlsTenantOnlyPolicies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Direct tenant_id tables
            migrationBuilder.Sql(@"
                ALTER TABLE branches ENABLE ROW LEVEL SECURITY;
                ALTER TABLE branches FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON branches USING (
                    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );

                ALTER TABLE users ENABLE ROW LEVEL SECURITY;
                ALTER TABLE users FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON users USING (
                    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );

                ALTER TABLE roles ENABLE ROW LEVEL SECURITY;
                ALTER TABLE roles FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON roles USING (
                    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );

                ALTER TABLE settings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE settings FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON settings USING (
                    tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
                );

                -- Subquery tables on roles
                ALTER TABLE role_permissions ENABLE ROW LEVEL SECURITY;
                ALTER TABLE role_permissions FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON role_permissions USING (
                    role_id IN (SELECT id FROM roles WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                );

                -- Subquery tables on users
                ALTER TABLE user_branches ENABLE ROW LEVEL SECURITY;
                ALTER TABLE user_branches FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON user_branches USING (
                    user_id IN (SELECT id FROM users WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                );

                ALTER TABLE user_role_assignments ENABLE ROW LEVEL SECURITY;
                ALTER TABLE user_role_assignments FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON user_role_assignments USING (
                    user_id IN (SELECT id FROM users WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                );

                ALTER TABLE refresh_tokens ENABLE ROW LEVEL SECURITY;
                ALTER TABLE refresh_tokens FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation_policy ON refresh_tokens USING (
                    user_id IN (SELECT id FROM users WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS tenant_isolation_policy ON refresh_tokens;
                ALTER TABLE refresh_tokens NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE refresh_tokens DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON user_role_assignments;
                ALTER TABLE user_role_assignments NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE user_role_assignments DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON user_branches;
                ALTER TABLE user_branches NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE user_branches DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON role_permissions;
                ALTER TABLE role_permissions NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE role_permissions DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON settings;
                ALTER TABLE settings NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE settings DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON roles;
                ALTER TABLE roles NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE roles DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON users;
                ALTER TABLE users NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE users DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS tenant_isolation_policy ON branches;
                ALTER TABLE branches NO FORCE ROW LEVEL SECURITY;
                ALTER TABLE branches DISABLE ROW LEVEL SECURITY;
            ");
        }
    }
}
