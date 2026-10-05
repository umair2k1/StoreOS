using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Customers;

public class Customer
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Phone { get; private set; }
    public string? Gstin { get; private set; }
    public decimal TotalPurchases { get; private set; }
    public decimal? CreditLimit { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Customer() { }

    public static Customer Create(Guid tenantId, string name, string? phone, string? gstin)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));
        return new Customer
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Name = name.Trim(),
            Phone = phone,
            Gstin = gstin,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(string name, string? phone, string? gstin)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));
        Name = name.Trim();
        Phone = phone;
        Gstin = gstin;
    }

    public void AddToTotalPurchases(decimal amount)
    {
        if (amount < 0) throw new DomainValidationException("invalid_amount", "Amount cannot be negative.");
        TotalPurchases += amount;
    }

    public void SubtractFromTotalPurchases(decimal amount)
    {
        if (amount < 0) throw new DomainValidationException("invalid_amount", "Amount cannot be negative.");
        TotalPurchases = Math.Max(0, TotalPurchases - amount);
    }

    public void SetCreditLimit(decimal? limit)
    {
        if (limit is not null && limit < 0)
            throw new DomainValidationException("invalid_credit_limit", "Credit limit cannot be negative.");
        CreditLimit = limit;
    }
}
