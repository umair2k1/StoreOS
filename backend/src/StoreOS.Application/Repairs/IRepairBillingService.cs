namespace StoreOS.Application.Repairs;

public interface IRepairBillingService
{
    Task LinkSaleAsync(Guid repairJobId, Guid saleId, string invoiceNumber, CancellationToken ct);
}
