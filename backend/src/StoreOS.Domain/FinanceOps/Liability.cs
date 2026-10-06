using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.FinanceOps;

public class Liability
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Type { get; private set; } = default!;
    public string? LenderName { get; private set; }
    public decimal TotalPrincipal { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public string? Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Liability() { }

    public static Liability Create(
        Guid tenantId,
        string title,
        string type,
        string? lenderName,
        decimal totalPrincipal,
        DateOnly? dueDate,
        string? notes)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainValidationException("liability_title_required", "Title is required.");

        if (!LiabilityType.All.Contains(type))
            throw new DomainValidationException("invalid_liability_type", $"Unknown liability type '{type}'.");

        if (totalPrincipal <= 0)
            throw new DomainValidationException("invalid_principal", "Total principal must be greater than zero.");

        return new Liability
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Title = title.Trim(),
            Type = type,
            LenderName = lenderName,
            TotalPrincipal = totalPrincipal,
            DueDate = dueDate,
            Status = "Active",
            Notes = notes,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateDescriptiveData(string title, string? lenderName, DateOnly? dueDate, string? status, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new DomainValidationException("liability_title_required", "Title is required.");

        Title = title.Trim();
        LenderName = lenderName;
        DueDate = dueDate;
        Status = status;
        Notes = notes;
    }
}
