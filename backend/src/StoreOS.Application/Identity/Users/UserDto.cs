namespace StoreOS.Application.Identity.Users;

public record UserDto(
    Guid Id,
    string Email,
    string DisplayName,
    bool IsActive,
    int TokenVersion,
    IReadOnlyList<UserRoleAssignmentDto> Roles,
    IReadOnlyList<UserBranchAssignmentDto> Branches);

public record UserRoleAssignmentDto(Guid RoleId, string RoleName, Guid? BranchId);
public record UserBranchAssignmentDto(Guid BranchId, string BranchName, string BranchCode, bool IsDefault);
