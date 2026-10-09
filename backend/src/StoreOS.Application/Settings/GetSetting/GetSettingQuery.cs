using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Settings.GetSetting;

public record GetSettingQuery(string Key, Guid? BranchId = null) : IRequest<string?>, IReadOnlyRequest, IRequiresPermission
{
    public string RequiredPermission => PermissionCatalog.SettingsManage;
}
