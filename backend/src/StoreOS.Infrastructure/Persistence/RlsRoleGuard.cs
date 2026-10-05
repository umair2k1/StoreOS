using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace StoreOS.Infrastructure.Persistence;

public class RlsRoleGuard : IHostedService
{
    private readonly IConfiguration _configuration;

    public RlsRoleGuard(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var cmd = new NpgsqlCommand(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user", conn);
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);

        if (await reader.ReadAsync(cancellationToken))
        {
            var isSuperuser = reader.GetBoolean(0);
            var bypassRls = reader.GetBoolean(1);

            if (isSuperuser || bypassRls)
            {
                throw new InvalidOperationException("Refusing to start: the database role bypasses row level security.");
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
