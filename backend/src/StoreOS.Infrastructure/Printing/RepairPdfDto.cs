namespace StoreOS.Infrastructure.Printing;

public record RepairPartLineDto(string PartName, int Quantity, decimal ChargeAmount);

public record RepairPdfDto(
    string ShopName,
    string? ShopAddress,
    string? ShopPhone,
    string JobNumber,
    DateTimeOffset ReceivedAt,
    string CustomerName,
    string? CustomerPhone,
    string? DeviceBrand,
    string? DeviceModel,
    string? SerialNumber,
    string? Imei1,
    string? ProblemDescription,
    string Status,
    decimal? EstimatedCost,
    decimal? FinalTotal,
    decimal? AdvancePaid,
    decimal? BalanceDue,
    DateOnly? WarrantyUntil,
    IReadOnlyList<RepairPartLineDto> Parts,
    string? InvoiceNumber,
    byte[]? LogoBytes = null
);
