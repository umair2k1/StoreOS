using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Inventory;

public class InventoryMovement
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? ImeiId { get; private set; }
    public string MovementType { get; private set; } = default!;
    public int QtyDelta { get; private set; }
    public decimal UnitCost { get; private set; }
    public string RefType { get; private set; } = default!;
    public string RefId { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? CreatedBy { get; private set; }

    private InventoryMovement() { }

    public static InventoryMovement Create(Guid tenantId, Guid branchId, Guid productId, Guid? imeiId,
        string movementType, int qtyDelta, decimal unitCost, string refType, string refId, Guid? createdBy)
    {
        if (!StoreOS.Domain.Inventory.MovementType.All.Contains(movementType))
            throw new DomainValidationException("invalid_movement_type", $"Unknown movement type '{movementType}'.");
        if (qtyDelta == 0)
            throw new DomainValidationException("invalid_qty_delta", "Quantity delta cannot be zero.");
        if (string.IsNullOrWhiteSpace(refType)) throw new ArgumentException("RefType is required.", nameof(refType));
        if (string.IsNullOrWhiteSpace(refId)) throw new ArgumentException("RefId is required.", nameof(refId));
        return new InventoryMovement
        {
            TenantId = tenantId,
            BranchId = branchId,
            ProductId = productId,
            ImeiId = imeiId,
            MovementType = movementType,
            QtyDelta = qtyDelta,
            UnitCost = unitCost,
            RefType = refType,
            RefId = refId,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
