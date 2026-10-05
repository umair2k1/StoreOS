namespace StoreOS.Domain.Platform;

public static class ImportSourceType
{
    public const string SupplierInvoice = "SupplierInvoice";
    public const string Excel = "Excel";
    public const string JsonBackup = "JsonBackup";
}

public static class ImportStatus
{
    public const string Pending = "Pending";
    public const string Imported = "Imported";
    public const string Failed = "Failed";
}

public class ImportBatch
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string SourceType { get; private set; } = default!;
    public string SourceRef { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public string? RecordCounts { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private ImportBatch() { }

    public static ImportBatch Create(Guid tenantId, Guid? branchId, string sourceType, string sourceRef, Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(sourceType)) throw new ArgumentException("SourceType is required.", nameof(sourceType));
        if (string.IsNullOrWhiteSpace(sourceRef)) throw new ArgumentException("SourceRef is required.", nameof(sourceRef));
        return new ImportBatch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            SourceType = sourceType,
            SourceRef = sourceRef,
            Status = ImportStatus.Pending,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkImported(string? recordCountsJson)
    {
        Status = ImportStatus.Imported;
        RecordCounts = recordCountsJson;
    }

    public void MarkFailed(string? reasonJson)
    {
        Status = ImportStatus.Failed;
        RecordCounts = reasonJson;
    }

    public void UpdateRecordCounts(string? recordCountsJson)
    {
        RecordCounts = recordCountsJson;
    }
}
