using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Suppliers;

public class SupplierReturn
{
    private readonly List<SupplierReturnItem> _items = new();

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string ReturnNumber { get; private set; } = default!;
    public DateOnly ReturnDate { get; private set; }
    public Guid SupplierId { get; private set; }
    public string? SupplierPhone { get; private set; }
    public string? SupplierAddress { get; private set; }
    public int TotalQty { get; private set; }
    public decimal TotalAmount { get; private set; }
    public string SettlementType { get; private set; } = default!;
    public string? Reason { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public IReadOnlyList<SupplierReturnItem> Items => _items.AsReadOnly();

    private SupplierReturn() { }

    public static SupplierReturn Create(
        Guid tenantId,
        Guid branchId,
        string returnNumber,
        DateOnly returnDate,
        Guid supplierId,
        string? supplierPhone,
        string? supplierAddress,
        string settlementType,
        string? reason,
        string? notes,
        Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(returnNumber))
            throw new DomainValidationException("invalid_return_number", "Return number is required.");

        if (!Suppliers.SettlementType.IsValid(settlementType))
            throw new DomainValidationException("invalid_settlement_type", $"Unknown settlement type '{settlementType}'.");

        return new SupplierReturn
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            ReturnNumber = returnNumber,
            ReturnDate = returnDate,
            SupplierId = supplierId,
            SupplierPhone = supplierPhone,
            SupplierAddress = supplierAddress,
            TotalQty = 0,
            TotalAmount = 0m,
            SettlementType = settlementType,
            Reason = reason,
            Notes = notes,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void AddItem(SupplierReturnItem item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        _items.Add(item);
        TotalQty += item.Qty;
        TotalAmount += item.LineTotal;
    }

    public SupplierReturnItem AddItem(
        Guid? productId,
        string itemName,
        int qty,
        decimal purchasePrice,
        decimal returnRate,
        string[]? imeiNumbers = null)
    {
        var item = SupplierReturnItem.Create(
            TenantId,
            Id,
            productId,
            itemName,
            qty,
            purchasePrice,
            returnRate,
            imeiNumbers);

        AddItem(item);
        return item;
    }
}
