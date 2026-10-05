namespace StoreOS.Application.Identity;

public interface IPermissionChecker
{
    bool IsValidPermissionCode(string code);
    IReadOnlyList<string> AllCodes { get; }
}
