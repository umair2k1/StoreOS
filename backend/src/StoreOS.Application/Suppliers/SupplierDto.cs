namespace StoreOS.Application.Suppliers;

public record SupplierDto(
    Guid Id,
    string Name,
    string? Phone,
    string? Address,
    string? BrandOrCategory,
    string? Notes,
    DateTimeOffset CreatedAt);
