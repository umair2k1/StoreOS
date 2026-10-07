using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Platform.LogoUpload;

public record LogoImage(byte[] Bytes, string ContentType, string ETag);

public record GetTenantLogoQuery(int Size, Guid TenantId) : IRequest<LogoImage>, IReadOnlyRequest, IAnonymousTenantRequest
{
    public Guid TenantId { get; init; } = TenantId;
}
