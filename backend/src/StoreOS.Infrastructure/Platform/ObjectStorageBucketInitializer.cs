using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Platform;

public class ObjectStorageBucketInitializer : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ObjectStorageBucketInitializer> _logger;

    public ObjectStorageBucketInitializer(
        IServiceProvider serviceProvider,
        ILogger<ObjectStorageBucketInitializer> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var storage = scope.ServiceProvider.GetRequiredService<IObjectStorageService>();
            await storage.EnsureBucketsAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not ensure object storage buckets on startup. MinIO may not be available.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
