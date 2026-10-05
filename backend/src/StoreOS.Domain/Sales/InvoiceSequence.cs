namespace StoreOS.Domain.Sales;

public class InvoiceSequence
{
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string SeriesKey { get; private set; } = default!;
    public int LastSeq { get; private set; }

    private InvoiceSequence() { }

    public static InvoiceSequence Create(Guid tenantId, Guid branchId, string seriesKey, int lastSeq = 0)
    {
        return new InvoiceSequence
        {
            TenantId = tenantId,
            BranchId = branchId,
            SeriesKey = seriesKey,
            LastSeq = lastSeq
        };
    }
}
