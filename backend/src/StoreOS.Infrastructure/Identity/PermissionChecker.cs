using StoreOS.Application.Identity;
using StoreOS.Domain.Identity;

namespace StoreOS.Infrastructure.Identity;

public class PermissionChecker : IPermissionChecker
{
    public bool IsValidPermissionCode(string code) => PermissionCatalog.IsValid(code);
    public IReadOnlyList<string> AllCodes => PermissionCatalog.All;
}
