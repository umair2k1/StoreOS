namespace StoreOS.Application.FinanceOps.Liabilities;

public record LiabilityDto(
    Guid Id,
    string Title,
    string Type,
    string? LenderName,
    decimal TotalPrincipal,
    decimal CurrentBalance,
    DateOnly? DueDate,
    string? Status,
    string? Notes);
