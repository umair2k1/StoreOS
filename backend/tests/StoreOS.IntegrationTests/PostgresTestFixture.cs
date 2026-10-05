using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace StoreOS.IntegrationTests;

/// <summary>
/// OwnerConnectionString: superuser/owner. Use ONLY for migrations, seeding cross-tenant fixture data and
/// schema inspection. AppConnectionString: role storeos_app (NOSUPERUSER, NOBYPASSRLS) - the ONLY connection
/// that may be used to assert RLS behaviour or to build the application under test.
/// </summary>
public sealed class PostgresTestFixture : IAsyncLifetime
{
    private const string AppPassword = "storeos_app_test_pw";
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine").WithDatabase("storeos_test")
        .WithUsername("storeos_owner").WithPassword("storeos_owner_test_pw").Build();

    public string OwnerConnectionString => _container.GetConnectionString();
    public string AppConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using (var conn = new NpgsqlConnection(OwnerConnectionString))
        {
            await conn.OpenAsync();
            await Exec(conn, $"CREATE ROLE storeos_app LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE");
            await Exec(conn, "GRANT CONNECT ON DATABASE storeos_test TO storeos_app");
            await Exec(conn, "GRANT USAGE ON SCHEMA public TO storeos_app");
            await Exec(conn, "ALTER DEFAULT PRIVILEGES FOR ROLE storeos_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO storeos_app");
            await Exec(conn, "ALTER DEFAULT PRIVILEGES FOR ROLE storeos_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO storeos_app");
        }
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(OwnerConnectionString).Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
            await db.Database.MigrateAsync();
        await ApplyPostMigrateAsync();
        AppConnectionString = new NpgsqlConnectionStringBuilder(OwnerConnectionString)
        { Username = "storeos_app", Password = AppPassword, MaxPoolSize = 20 }.ToString();
    }

    /// <summary>
    /// Re-run after any test that adds tables/migrations. Reads deploy/db/post-migrate.sql (P-002).
    /// </summary>
    public async Task ApplyPostMigrateAsync()
    {
        var sql = await File.ReadAllTextAsync(FindRepoFile("deploy/db/post-migrate.sql"));
        await using var conn = new NpgsqlConnection(OwnerConnectionString);
        await conn.OpenAsync();
        await Exec(conn, sql);
    }

    private static async Task Exec(NpgsqlConnection c, string sql)
    {
        await using var cmd = new NpgsqlCommand(sql, c);
        await cmd.ExecuteNonQueryAsync();
    }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relative)))
            dir = dir.Parent;
        return dir is null ? throw new FileNotFoundException(relative) : Path.Combine(dir.FullName, relative);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresTestFixture>
{
}
