namespace StoreOS.Application.Repairs.Parts;

public record RepairJobPartDto(
    Guid Id,
    Guid RepairJobId,
    Guid? ProductId,
    string PartName,
    int Quantity,
    decimal UnitCost,
    decimal ChargeAmount,
    string Status,
    DateTimeOffset CreatedAt);
