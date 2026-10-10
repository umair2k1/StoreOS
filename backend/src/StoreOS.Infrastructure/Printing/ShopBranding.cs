namespace StoreOS.Infrastructure.Printing;

public record ShopBranding(
    string ShopName,
    string? Address = null,
    string? Phone = null,
    string? Email = null,
    string? Gstin = null,
    byte[]? LogoBytes = null
);
