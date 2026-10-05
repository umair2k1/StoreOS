namespace StoreOS.Application.Abstractions;

public interface IAnonymousTenantRequest
{
    Guid TenantId { get; }
}
