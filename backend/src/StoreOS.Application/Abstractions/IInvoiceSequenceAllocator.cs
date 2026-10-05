namespace StoreOS.Application.Abstractions;

public interface IInvoiceSequenceAllocator
{
    Task<int> AllocateNextAsync(Guid tenantId, Guid branchId, string seriesKey, CancellationToken ct);
}
