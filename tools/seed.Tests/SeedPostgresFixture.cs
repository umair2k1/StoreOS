using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Seed.Tests;

public sealed class SeedPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("seed_test")
        .WithUsername("storeos_owner")
        .WithPassword("storeos_owner_pw")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // Ensure storeos_app role exists for migrations that grant permissions to it
        await using (var conn = new NpgsqlConnection(ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DO $$ BEGIN IF NOT EXISTS (SELECT FROM pg_roles WHERE rolname = 'storeos_app') THEN " +
                "CREATE ROLE storeos_app LOGIN PASSWORD 'storeos_app_test_pw' NOSUPERUSER NOBYPASSRLS NOCREATEROLE NOCREATEDB; " +
                "END IF; END $$; " +
                "GRANT USAGE ON SCHEMA public TO storeos_app;", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        // 1. Run EF migrations
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            await db.Database.MigrateAsync();
        }

        // 2. Apply post-migrate.sql
        var sqlPath = FindRepoFile("deploy/db/post-migrate.sql");
        if (File.Exists(sqlPath))
        {
            var sql = await File.ReadAllTextAsync(sqlPath);
            await using var conn = new NpgsqlConnection(ConnectionString);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(sql, conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public StoreOsDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;
        return new StoreOsDbContext(options, new NullAccessScope());
    }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relative)))
        {
            dir = dir.Parent;
        }
        return dir is null ? relative : Path.Combine(dir.FullName, relative);
    }
}

[CollectionDefinition("seed-postgres")]
public class SeedPostgresCollection : ICollectionFixture<SeedPostgresFixture>
{
}
