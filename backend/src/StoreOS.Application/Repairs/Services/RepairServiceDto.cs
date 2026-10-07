namespace StoreOS.Application.Repairs.Services;

public record RepairServiceDto(
    Guid Id,
    string Name,
    string? Description,
    decimal StandardCharge,
    bool Taxable,
    decimal GstRate,
    string? SacCode,
    int? EstimatedDurationMinutes,
    int? WarrantyDays,
    bool IsActive);
