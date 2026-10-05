namespace StoreOS.Application.Abstractions;

public interface IRequiresPermission
{
    string RequiredPermission { get; }
}
