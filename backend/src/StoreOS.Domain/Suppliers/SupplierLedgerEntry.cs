using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Suppliers;

/// <summary>
/// Supplier ledger entry.
/// IMPORTANT POLARITY CONVENTION:
/// Supplier ledger follows the opposite polarity of the customer ledger:
/// Credit increases payable (Bills, OpeningBalance).
/// Debit decreases payable (Payments, ReturnCredit).
/// Current payable = SUM(credit) - SUM(debit).
/// </summary>
public class SupplierLedgerEntry
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SupplierId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string EntryType { get; private set; } = default!;
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public DateOnly EntryDate { get; private set; }
    public string? RefId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private SupplierLedgerEntry() { }

    public static SupplierLedgerEntry CreateDebit(
        Guid tenantId,
        Guid supplierId,
        Guid? branchId,
        string entryType,
        decimal amount,
        DateOnly entryDate,
        string? refId)
    {
        Validate(entryType, amount);
        return new SupplierLedgerEntry
        {
            TenantId = tenantId,
            SupplierId = supplierId,
            BranchId = branchId,
            EntryType = entryType,
            Debit = amount,
            Credit = 0m,
            EntryDate = entryDate,
            RefId = refId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static SupplierLedgerEntry CreateCredit(
        Guid tenantId,
        Guid supplierId,
        Guid? branchId,
        string entryType,
        decimal amount,
        DateOnly entryDate,
        string? refId)
    {
        Validate(entryType, amount);
        return new SupplierLedgerEntry
        {
            TenantId = tenantId,
            SupplierId = supplierId,
            BranchId = branchId,
            EntryType = entryType,
            Debit = 0m,
            Credit = amount,
            EntryDate = entryDate,
            RefId = refId,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    private static void Validate(string entryType, decimal amount)
    {
        if (!SupplierLedgerEntryType.IsValid(entryType))
            throw new DomainValidationException("invalid_ledger_entry_type", $"Unknown entry type '{entryType}'.");
        if (amount <= 0)
            throw new DomainValidationException("invalid_amount", "Ledger amount must be positive.");
    }
}
