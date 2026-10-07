using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Settings.PatchSetting;

public record PatchSettingCommand(string Key, string ValueJson, Guid? BranchId = null) : IRequest<Unit>, IRequiresPermission
{
    public string RequiredPermission => PermissionCatalog.SettingsManage;
}
