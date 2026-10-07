using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Platform.LogoUpload;

public record UploadLogoCommand(byte[] FileBytes, string DeclaredContentType) : IRequest<UploadLogoResult>, IRequiresPermission
{
    public string RequiredPermission => PermissionCatalog.SettingsManage;
}

public record UploadLogoResult(string LogoUrl);
