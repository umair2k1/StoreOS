using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform.BackupExport;

namespace StoreOS.Application.Platform.BackupImport;

public record ImportTenantDataCommand(TenantBackupDto Backup)
    : IRequest<ImportResultDto>, IRequiresPermission, IHighImpactOperation, IIdempotentRequest
{
    public string RequiredPermission => "settings:manage";
    public string? IdempotencyKey { get; init; }
    public string IdempotencyScope => "tenant.import";
    public bool IdempotencyRequired => true;
}

public record ImportResultDto(bool Success, Dictionary<string, int> RecordCounts, string? ErrorMessage);
