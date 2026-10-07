using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Platform.BackupExport;

public record ExportTenantDataCommand() : IRequest<TenantBackupDto>, IRequiresPermission, IHighImpactOperation
{
    public string RequiredPermission => "settings:manage";
}
