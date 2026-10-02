using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StoreOS.Application.Abstractions;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Tenancy;

public class TenantResolver : ITenantResolver
{
    private static readonly Regex SubdomainRegex = new(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly string[] DefaultBaseDomains = ["pos.storeos.io", "localhost"];

    private readonly StoreOsDbContext _dbContext;
    private readonly string[] _baseDomains;

    public TenantResolver(StoreOsDbContext dbContext, IConfiguration configuration)
    {
        _dbContext = dbContext;
        _baseDomains = configuration.GetSection("Tenancy:BaseDomains").Get<string[]>() ?? DefaultBaseDomains;
    }

    public async Task<TenantPublicProfile?> ResolveFromHostAsync(string host, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(host))
            return null;

        var hostWithoutPort = host.Contains(':') ? host.Split(':')[0].Trim() : host.Trim();
        var normalizedHost = hostWithoutPort.ToLowerInvariant();

        string? matchedSubdomain = null;

        foreach (var baseDomain in _baseDomains)
        {
            var suffix = "." + baseDomain.Trim().ToLowerInvariant();
            if (normalizedHost.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                var candidate = normalizedHost[..^suffix.Length];
                if (!string.IsNullOrEmpty(candidate) && SubdomainRegex.IsMatch(candidate))
                {
                    matchedSubdomain = candidate;
                    break;
                }
            }
        }

        if (matchedSubdomain is null)
            return null;

        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Subdomain == matchedSubdomain)
            .Select(t => new TenantPublicProfile(t.Id, t.Name, t.Subdomain, t.IsActive))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<TenantPublicProfile?> ResolveByIdAsync(Guid tenantId, CancellationToken ct = default)
    {
        return await _dbContext.Tenants
            .AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantPublicProfile(t.Id, t.Name, t.Subdomain, t.IsActive))
            .FirstOrDefaultAsync(ct);
    }
}
