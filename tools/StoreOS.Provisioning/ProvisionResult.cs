namespace StoreOS.Provisioning;

public record ProvisionResult
{
    public bool Success { get; init; }
    public int ExitCode { get; init; }
    public string? Error { get; init; }
    public Guid TenantId { get; init; }
    public Guid BranchId { get; init; }
    public Guid OwnerUserId { get; init; }
    public string Subdomain { get; init; } = string.Empty;
    public string OwnerPassword { get; init; } = string.Empty;
    public string LoginUrl { get; init; } = string.Empty;

    public static ProvisionResult Succeeded(
        Guid tenantId,
        Guid branchId,
        Guid ownerUserId,
        string subdomain,
        string ownerPassword,
        string loginUrl) => new()
    {
        Success = true,
        ExitCode = 0,
        TenantId = tenantId,
        BranchId = branchId,
        OwnerUserId = ownerUserId,
        Subdomain = subdomain,
        OwnerPassword = ownerPassword,
        LoginUrl = loginUrl
    };

    public static ProvisionResult Failed(string error, int exitCode = 1) => new()
    {
        Success = false,
        ExitCode = exitCode,
        Error = error
    };
}
