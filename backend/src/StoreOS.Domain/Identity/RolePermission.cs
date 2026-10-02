namespace StoreOS.Domain.Identity;

public class RolePermission
{
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = default!;

    private RolePermission() { }

    public static RolePermission Create(Guid roleId, string permissionCode)
    {
        if (string.IsNullOrWhiteSpace(permissionCode))
            throw new ArgumentException("Permission code required.", nameof(permissionCode));
        return new RolePermission
        {
            RoleId = roleId,
            PermissionCode = permissionCode.Trim()
        };
    }
}
