namespace StoreOS.Application.Abstractions;

public record TenantPublicProfile(Guid TenantId, string Name, string Subdomain, bool IsActive);

public interface ITenantResolver
{
    /// <summary>
    /// Resolves tenant from host like "apex.pos.storeos.io", "apex.localhost:4200"; null when no tenant matches.
    /// </summary>
    Task<TenantPublicProfile?> ResolveFromHostAsync(string host, CancellationToken ct = default);

    Task<TenantPublicProfile?> ResolveByIdAsync(Guid tenantId, CancellationToken ct = default);
}
