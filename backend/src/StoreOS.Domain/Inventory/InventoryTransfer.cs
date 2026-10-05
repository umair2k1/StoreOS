using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Inventory;

public static class TransferStatus
{
    public const string Pending = "Pending";
    public const string Received = "Received";
}

public class InventoryTransfer
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FromBranchId { get; private set; }
    public Guid ToBranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Qty { get; private set; }
    public string Status { get; private set; } = default!;
    public Guid[]? ImeiIds { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? ReceivedBy { get; private set; }
    public DateTimeOffset? ReceivedAt { get; private set; }

    private InventoryTransfer() { }

    public static InventoryTransfer CreatePending(Guid tenantId, Guid fromBranchId, Guid toBranchId,
        Guid productId, int qty, Guid[]? imeiIds, Guid createdBy)
    {
        if (fromBranchId == toBranchId)
            throw new DomainValidationException("invalid_transfer_destination", "Cannot transfer to the same branch.");
        if (qty <= 0)
            throw new DomainValidationException("invalid_quantity", "Transfer quantity must be positive.");

        return new InventoryTransfer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            FromBranchId = fromBranchId,
            ToBranchId = toBranchId,
            ProductId = productId,
            Qty = qty,
            Status = TransferStatus.Pending,
            ImeiIds = imeiIds,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkReceived(Guid receivedBy)
    {
        if (Status == TransferStatus.Received)
            throw new DomainValidationException("transfer_already_received", "Transfer has already been received.");

        Status = TransferStatus.Received;
        ReceivedBy = receivedBy;
        ReceivedAt = DateTimeOffset.UtcNow;
    }
}
