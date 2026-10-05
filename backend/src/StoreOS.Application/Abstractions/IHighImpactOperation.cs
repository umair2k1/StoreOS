namespace StoreOS.Application.Abstractions;

public interface IHighImpactOperation
{
    string RequiredPermission { get; }
}
