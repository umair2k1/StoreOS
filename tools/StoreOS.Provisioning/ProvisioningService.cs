using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Platform;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Provisioning;

public class ProvisioningService
{
    private static readonly Regex SubdomainRegex = new(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex BranchCodeRegex = new(@"^[A-Z1-9][A-Z0-9]{0,2}$", RegexOptions.Compiled);
    private static readonly Regex GstinRegex = new(@"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$", RegexOptions.Compiled);
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled);

    private readonly StoreOsDbContext? _dbContext;
    private readonly string? _connectionString;

    public ProvisioningService(StoreOsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public ProvisioningService(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task<ProvisionResult> ProvisionAsync(ProvisionRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Tenant name is required.", nameof(request.Name));

        if (string.IsNullOrWhiteSpace(request.Subdomain))
            throw new ArgumentException("Subdomain is required.", nameof(request.Subdomain));

        var normalizedSubdomain = request.Subdomain.Trim().ToLowerInvariant();
        if (!SubdomainRegex.IsMatch(normalizedSubdomain))
            throw new ArgumentException("Subdomain must match lowercase letters, digits and hyphens.", nameof(request.Subdomain));

        var branchCode = (request.BranchCode ?? string.Empty).Trim().ToUpperInvariant();
        if (!BranchCodeRegex.IsMatch(branchCode))
            throw new ArgumentException("Branch code must be 1-3 uppercase alphanumeric characters, not starting with 0.", nameof(request.BranchCode));

        if (string.IsNullOrWhiteSpace(request.BranchName))
            throw new ArgumentException("Branch name is required.", nameof(request.BranchName));

        if (string.IsNullOrWhiteSpace(request.OwnerEmail) || !EmailRegex.IsMatch(request.OwnerEmail.Trim()))
            throw new ArgumentException("Owner email is invalid.", nameof(request.OwnerEmail));

        if (string.IsNullOrWhiteSpace(request.OwnerName))
            throw new ArgumentException("Owner name is required.", nameof(request.OwnerName));

        string? gstin = null;
        if (!string.IsNullOrWhiteSpace(request.Gstin))
        {
            gstin = request.Gstin.Trim().ToUpperInvariant();
            if (!GstinRegex.IsMatch(gstin))
                throw new ArgumentException("GSTIN must match 15-character GSTIN pattern.", nameof(request.Gstin));
        }

        StoreOsDbContext db;
        bool disposeDb = false;

        if (_dbContext != null)
        {
            db = _dbContext;
        }
        else
        {
            var cs = _connectionString
                ?? Environment.GetEnvironmentVariable("STOREOS_MIGRATIONS_CONNECTION")
                ?? "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password";

            var options = new DbContextOptionsBuilder<StoreOsDbContext>()
                .UseNpgsql(cs)
                .Options;

            db = new StoreOsDbContext(options, new NullAccessScope());
            disposeDb = true;
        }

        try
        {
            var existingTenant = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(t => t.Subdomain == normalizedSubdomain, ct);

            if (existingTenant != null)
            {
                return ProvisionResult.Failed($"Subdomain '{normalizedSubdomain}' already exists.", exitCode: 2);
            }

            var password = string.IsNullOrWhiteSpace(request.OwnerPassword)
                ? GenerateSecurePassword(16)
                : request.OwnerPassword;

            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var tenant = Tenant.Create(request.Name.Trim(), request.Timezone, normalizedSubdomain);
            tenant.SetFeatures(JsonSerializer.Serialize(new { repairs = request.Repairs }));
            await db.Tenants.AddAsync(tenant, ct);

            var branch = Branch.Create(tenant.Id, branchCode, request.BranchName.Trim());
            if (gstin != null)
            {
                branch.UpdateProfile(request.BranchName.Trim(), address: null, phone: null, email: null, gstin: gstin);
            }
            await db.Branches.AddAsync(branch, ct);

            var roleMap = new Dictionary<string, Role>(StringComparer.OrdinalIgnoreCase);
            foreach (var roleName in StandardRoles.All)
            {
                var role = Role.Create(tenant.Id, roleName);
                roleMap[roleName] = role;
                await db.Roles.AddAsync(role, ct);

                if (StandardRoles.RolePermissions.TryGetValue(roleName, out var permissions))
                {
                    foreach (var permCode in permissions)
                    {
                        var rolePerm = RolePermission.Create(role.Id, permCode);
                        await db.RolePermissions.AddAsync(rolePerm, ct);
                    }
                }
            }

            var hasher = new Argon2PasswordHasher();
            var passwordHash = hasher.Hash(password);
            var ownerUser = User.Create(tenant.Id, request.OwnerEmail.Trim().ToLowerInvariant(), passwordHash, request.OwnerName.Trim());
            await db.Users.AddAsync(ownerUser, ct);

            var userBranch = UserBranch.Create(ownerUser.Id, branch.Id, isDefault: true);
            await db.UserBranches.AddAsync(userBranch, ct);

            var auditEntry = AuditLogEntry.Create(
                tenantId: tenant.Id,
                branchId: null,
                correlationId: Guid.NewGuid().ToString(),
                userId: ownerUser.Id,
                action: "tenant.provisioned",
                entityType: "Tenant",
                entityId: tenant.Id.ToString(),
                changesJson: JsonSerializer.Serialize(new
                {
                    subdomain = tenant.Subdomain,
                    @operator = Environment.UserName
                })
            );
            await db.AuditLogEntries.AddAsync(auditEntry, ct);

            await db.SaveChangesAsync(ct);

            // Tenant-wide Owner role assignment (branch_id = null)
            // CODING_STANDARDS.md §3.2: Use raw SQL to bypass EF Core composite key null tracking
            var ownerRole = roleMap[StandardRoles.Owner];
            await db.ExecuteRawSqlAsync(
                "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES ({0}, {1}, NULL)",
                new object[] { ownerUser.Id, ownerRole.Id }, ct);

            await tx.CommitAsync(ct);

            var loginUrl = $"https://{tenant.Subdomain}.pos.storeos.io";
            return ProvisionResult.Succeeded(
                tenant.Id,
                branch.Id,
                ownerUser.Id,
                tenant.Subdomain,
                password,
                loginUrl
            );
        }
        finally
        {
            if (disposeDb)
            {
                await db.DisposeAsync();
            }
        }
    }

    private static string GenerateSecurePassword(int length = 16)
    {
        const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lower = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";
        const string special = "!@#$%^&*";
        const string all = upper + lower + digits + special;

        var chars = new char[length];
        chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
        chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
        chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
        chars[3] = special[RandomNumberGenerator.GetInt32(special.Length)];

        for (int i = 4; i < length; i++)
        {
            chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];
        }

        for (int i = length - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }
}
