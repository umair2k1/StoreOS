namespace StoreOS.Application.Abstractions;

public interface ITenantJobRunner
{
    Task RunAsync(Guid tenantId, Func<IServiceProvider, CancellationToken, Task> work, CancellationToken ct = default);
}
