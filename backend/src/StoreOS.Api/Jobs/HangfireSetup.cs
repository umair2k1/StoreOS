using Hangfire;
using Hangfire.PostgreSql;
using StoreOS.Infrastructure.Jobs;

namespace StoreOS.Api.Jobs;

public static class HangfireSetup
{
    private static readonly object _schemaLock = new();

    public static IServiceCollection AddHangfireConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddHangfire((_, config) =>
            {
                lock (_schemaLock)
                {
                    config
                        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                        .UseSimpleAssemblyNameTypeSerializer()
                        .UseRecommendedSerializerSettings()
                        .UsePostgreSqlStorage(options => options.UseNpgsqlConnection(connectionString));
                }
            });
            services.AddHangfireServer();
        }

        services.AddScoped<ReconciliationJob>();
        services.AddScoped<IdempotencyKeyPurgeJob>();
        return services;
    }

    public static IApplicationBuilder UseHangfireConfiguration(this IApplicationBuilder app)
    {
        try
        {
            RecurringJob.AddOrUpdate<ReconciliationJob>(
                "nightly-reconciliation",
                job => job.RunAsync(Guid.NewGuid().ToString("D"), CancellationToken.None),
                Cron.Daily);

            RecurringJob.AddOrUpdate<IdempotencyKeyPurgeJob>(
                "idempotency-key-purge",
                job => job.RunAsync(CancellationToken.None),
                Cron.Daily);
        }
        catch
        {
            // Safe fallback if Hangfire storage is not initialized
        }

        return app;
    }
}
