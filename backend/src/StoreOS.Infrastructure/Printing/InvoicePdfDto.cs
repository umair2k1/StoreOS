namespace StoreOS.Infrastructure.Printing;

public record InvoicePdfLineDto(
    string ItemName,
    string? Hsn,
    int Qty,
    decimal PricePerUnit,
    decimal Taxable,
    decimal Cgst,
    decimal Sgst,
    string? Imei,
    bool IsGift,
    bool IsMarginScheme = false,
    string? Category = null
);

public record InvoicePdfDto(
    string ShopName,
    string? ShopAddress,
    string? ShopPhone,
    string? ShopEmail,
    string? ShopGstin,
    string InvoiceNumber,
    string InvoiceType,
    DateTimeOffset SaleDate,
    string CustomerName,
    string? CustomerGstin,
    IReadOnlyList<InvoicePdfLineDto> Lines,
    decimal TotalTaxable,
    decimal TotalCgst,
    decimal TotalSgst,
    decimal Discount,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal Balance,
    string? PaymentMode,
    string? FinanceProvider,
    string? FinancerName,
    string? Description,
    string? IrnDisplay,
    string? IrnQrDataUrl, // both null until Phase 8 (roadmap) e-invoicing activates; slots reserved now
    byte[]? LogoBytes = null,
    string? CustomerPhone = null,
    decimal? DownPayment = null,
    string? EmiDetails = null
);
