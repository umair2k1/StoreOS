namespace StoreOS.Application.Tenancy.Branches;

public record BranchDto(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    string? Phone,
    string? Email,
    string? Gstin,
    bool IsActive);
