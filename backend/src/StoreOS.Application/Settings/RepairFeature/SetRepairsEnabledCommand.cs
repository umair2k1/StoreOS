using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Settings.RepairFeature;

public record SetRepairsEnabledCommand(bool Enabled) : IRequest, IRequiresPermission
{
    public string RequiredPermission => "settings:manage";
}
