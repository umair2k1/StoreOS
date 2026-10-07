using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Repairs;

public class RepairJobPart
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid RepairJobId { get; private set; }
    public Guid? ProductId { get; private set; }
    public string PartName { get; private set; } = default!;
    public int Quantity { get; private set; }
    public decimal UnitCost { get; private set; }
    public decimal ChargeAmount { get; private set; }
    public string Status { get; private set; } = default!;
    public DateTimeOffset CreatedAt { get; private set; }

    private RepairJobPart() { }

    public static RepairJobPart Reserve(Guid tenantId, Guid repairJobId, Guid? productId,
        string partName, int quantity, decimal unitCost, decimal chargeAmount)
    {
        if (quantity <= 0) throw new DomainValidationException("invalid_quantity", "Quantity must be positive.");
        if (string.IsNullOrWhiteSpace(partName)) throw new DomainValidationException("part_name_required", "Part name is required.");
        return new RepairJobPart
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            RepairJobId = repairJobId,
            ProductId = productId,
            PartName = partName.Trim(),
            Quantity = quantity,
            UnitCost = unitCost,
            ChargeAmount = chargeAmount,
            Status = RepairPartStatus.Reserved,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static RepairJobPart Rehydrate(
        Guid id, Guid tenantId, Guid repairJobId, Guid? productId, string partName,
        int quantity, decimal unitCost, decimal chargeAmount, string status, DateTimeOffset createdAt)
    {
        return new RepairJobPart
        {
            Id = id,
            TenantId = tenantId,
            RepairJobId = repairJobId,
            ProductId = productId,
            PartName = partName,
            Quantity = quantity,
            UnitCost = unitCost,
            ChargeAmount = chargeAmount,
            Status = status,
            CreatedAt = createdAt
        };
    }

    public void MarkConsumed()
    {
        if (Status != RepairPartStatus.Reserved)
            throw new DomainValidationException("part_not_reserved", "Only a Reserved part can be consumed.");
        Status = RepairPartStatus.Consumed;
    }

    public void MarkReturned()
    {
        if (Status == RepairPartStatus.Returned)
            throw new DomainValidationException("part_already_returned", "Part has already been returned.");
        Status = RepairPartStatus.Returned;
    }
}
