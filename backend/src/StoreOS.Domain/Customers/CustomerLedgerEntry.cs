using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Customers;

public class CustomerLedgerEntry
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? SaleId { get; private set; }
    public string EntryType { get; private set; } = default!;
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public Guid? BranchId { get; private set; }
    public DateOnly EntryDate { get; private set; }
    public string? RefId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomerLedgerEntry() { }

    public static CustomerLedgerEntry CreateDebit(
        Guid tenantId,
        Guid customerId,
        Guid? saleId,
        string entryType,
        decimal amount,
        Guid? branchId,
        DateOnly entryDate,
        string? refId)
    {
        Validate(entryType, amount);
        return new CustomerLedgerEntry
        {
            TenantId = tenantId,
            CustomerId = customerId,
            SaleId = saleId,
            EntryType = entryType,
            Debit = amount,
            Credit = 0m,
            BranchId = branchId,
            EntryDate = entryDate,
            RefId = refId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static CustomerLedgerEntry CreateCredit(
        Guid tenantId,
        Guid customerId,
        Guid? saleId,
        string entryType,
        decimal amount,
        Guid? branchId,
        DateOnly entryDate,
        string? refId)
    {
        Validate(entryType, amount);
        return new CustomerLedgerEntry
        {
            TenantId = tenantId,
            CustomerId = customerId,
            SaleId = saleId,
            EntryType = entryType,
            Debit = 0m,
            Credit = amount,
            BranchId = branchId,
            EntryDate = entryDate,
            RefId = refId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static CustomerLedgerEntry Rehydrate(
        long id, Guid tenantId, Guid customerId, Guid? saleId, string entryType,
        decimal debit, decimal credit, Guid? branchId, DateOnly entryDate, string? refId, DateTimeOffset createdAt)
    {
        return new CustomerLedgerEntry
        {
            Id = id,
            TenantId = tenantId,
            CustomerId = customerId,
            SaleId = saleId,
            EntryType = entryType,
            Debit = debit,
            Credit = credit,
            BranchId = branchId,
            EntryDate = entryDate,
            RefId = refId,
            CreatedAt = createdAt
        };
    }

    private static void Validate(string entryType, decimal amount)
    {
        if (!CustomerLedgerEntryType.All.Contains(entryType))
            throw new DomainValidationException("invalid_ledger_entry_type", $"Unknown entry type '{entryType}'.");
        if (amount <= 0)
            throw new DomainValidationException("invalid_amount", "Ledger amount must be positive.");
    }
}
