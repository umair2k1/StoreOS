using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Sales;

public class Sale
{
    private readonly List<SaleItem> _items = new();

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string InvoiceNumber { get; private set; } = default!;
    public string InvoiceType { get; private set; } = default!;
    public DateTimeOffset SaleDate { get; private set; }
    public Guid? CustomerId { get; private set; }
    public string? CustomerName { get; private set; }
    public string? CustomerGstin { get; private set; }
    public decimal TotalTaxable { get; private set; }
    public decimal TotalCgst { get; private set; }
    public decimal TotalSgst { get; private set; }
    public decimal GrandTotal { get; private set; }
    public decimal Discount { get; private set; }
    public decimal AmountPaid { get; private set; }
    public string Status { get; private set; } = default!;
    public string? PaymentMode { get; private set; }
    public string? FinanceProvider { get; private set; }
    public string? FinancerName { get; private set; }
    public decimal? DownPayment { get; private set; }
    public string? EmiDetailsJson { get; private set; }
    public string? GiftDetailsJson { get; private set; }
    public string? Description { get; private set; }
    public Guid? RepairJobId { get; private set; }
    public string? Irn { get; private set; }
    public string? IrnAckNo { get; private set; }
    public string? IrnQrData { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public Guid? ConvertedFromEstimateId { get; private set; }
    public Guid? LinkedTaxInvoiceId { get; private set; }

    public IReadOnlyList<SaleItem> Items => _items.AsReadOnly();

    private Sale() { }

    private static Sale CreateInternal(
        Guid tenantId,
        Guid branchId,
        string invoiceNumber,
        string invoiceType,
        DateTimeOffset saleDate,
        Guid? customerId,
        string? customerName,
        string? customerGstin,
        decimal totalTaxable,
        decimal totalCgst,
        decimal totalSgst,
        decimal grandTotal,
        decimal discount,
        decimal amountPaid,
        string? paymentMode,
        string? financeProvider,
        string? financerName,
        decimal? downPayment,
        string? emiDetailsJson,
        string? giftDetailsJson,
        string? description,
        Guid? repairJobId,
        Guid createdBy)
    {
        if (amountPaid > grandTotal)
        {
            throw new DomainValidationException("payment_exceeds_balance", "Initial amount paid cannot exceed grand total.");
        }

        if (paymentMode != null && !Sales.PaymentMode.All.Contains(paymentMode))
        {
            throw new DomainValidationException("invalid_payment_mode", $"Unknown payment mode '{paymentMode}'.");
        }

        return new Sale
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            InvoiceNumber = invoiceNumber,
            InvoiceType = invoiceType,
            SaleDate = saleDate,
            CustomerId = customerId,
            CustomerName = customerName,
            CustomerGstin = customerGstin,
            TotalTaxable = totalTaxable,
            TotalCgst = totalCgst,
            TotalSgst = totalSgst,
            GrandTotal = grandTotal,
            Discount = discount,
            AmountPaid = amountPaid,
            Status = SaleStatus.Active,
            PaymentMode = paymentMode,
            FinanceProvider = financeProvider,
            FinancerName = financerName,
            DownPayment = downPayment,
            EmiDetailsJson = emiDetailsJson,
            GiftDetailsJson = giftDetailsJson,
            Description = description,
            RepairJobId = repairJobId,
            CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public static Sale CreateTaxInvoice(
        Guid tenantId,
        Guid branchId,
        string invoiceNumber,
        DateTimeOffset saleDate,
        Guid? customerId,
        string? customerName,
        string? customerGstin,
        decimal totalTaxable,
        decimal totalCgst,
        decimal totalSgst,
        decimal grandTotal,
        decimal discount,
        decimal amountPaid,
        string? paymentMode,
        string? financeProvider,
        string? financerName,
        decimal? downPayment,
        string? emiDetailsJson,
        string? giftDetailsJson,
        string? description,
        Guid? repairJobId,
        Guid createdBy)
    {
        return CreateInternal(
            tenantId, branchId, invoiceNumber, Sales.InvoiceType.TaxInvoice, saleDate,
            customerId, customerName, customerGstin, totalTaxable, totalCgst, totalSgst,
            grandTotal, discount, amountPaid, paymentMode, financeProvider, financerName,
            downPayment, emiDetailsJson, giftDetailsJson, description, repairJobId, createdBy);
    }

    public static Sale CreateEstimate(
        Guid tenantId,
        Guid branchId,
        string invoiceNumber,
        DateTimeOffset saleDate,
        Guid? customerId,
        string? customerName,
        string? customerGstin,
        decimal totalTaxable,
        decimal totalCgst,
        decimal totalSgst,
        decimal grandTotal,
        decimal discount,
        decimal amountPaid,
        string? paymentMode,
        string? financeProvider,
        string? financerName,
        decimal? downPayment,
        string? emiDetailsJson,
        string? giftDetailsJson,
        string? description,
        Guid? repairJobId,
        Guid createdBy)
    {
        return CreateInternal(
            tenantId, branchId, invoiceNumber, Sales.InvoiceType.Estimate, saleDate,
            customerId, customerName, customerGstin, totalTaxable, totalCgst, totalSgst,
            grandTotal, discount, amountPaid, paymentMode, financeProvider, financerName,
            downPayment, emiDetailsJson, giftDetailsJson, description, repairJobId, createdBy);
    }

    public void AddItem(SaleItem item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        _items.Add(item);
    }

    public void MarkVoided()
    {
        if (Status != SaleStatus.Active)
        {
            throw new DomainValidationException("sale_not_active", $"Cannot void sale with status '{Status}'.");
        }

        Status = SaleStatus.Voided;
    }

    public void MarkRefunded()
    {
        if (Status != SaleStatus.Active)
        {
            throw new DomainValidationException("sale_not_active", $"Cannot refund sale with status '{Status}'.");
        }

        Status = SaleStatus.Refunded;
    }

    public void ApplyPayment(decimal amount)
    {
        if (amount <= 0)
        {
            throw new DomainValidationException("invalid_payment_amount", "Payment amount must be greater than zero.");
        }

        if (AmountPaid + amount > GrandTotal)
        {
            throw new DomainValidationException("payment_exceeds_balance", $"Payment amount {amount} exceeds remaining balance {GrandTotal - AmountPaid}.");
        }

        AmountPaid += amount;
    }

    public void SetIrn(string irn, string irnAckNo, string irnQrData)
    {
        Irn = irn;
        IrnAckNo = irnAckNo;
        IrnQrData = irnQrData;
    }

    public void RecordFinanceDetails(string financeProvider, string? financerName, decimal downPayment, string emiDetailsJson)
    {
        PaymentMode = Sales.PaymentMode.Finance;
        FinanceProvider = financeProvider;
        FinancerName = financerName;
        DownPayment = downPayment;
        EmiDetailsJson = emiDetailsJson;
    }

    public void MarkConvertedFrom(Guid estimateId) => ConvertedFromEstimateId = estimateId;

    public void MarkConvertedToTaxInvoice(Guid taxInvoiceId)
    {
        if (InvoiceType != StoreOS.Domain.Sales.InvoiceType.Estimate)
            throw new StoreOS.Domain.Exceptions.DomainValidationException("not_an_estimate",
                "Only an Estimate can be marked as converted.");
        if (LinkedTaxInvoiceId is not null)
            throw new StoreOS.Domain.Exceptions.DomainValidationException("estimate_already_converted",
                "This estimate has already been converted to a tax invoice.");
        LinkedTaxInvoiceId = taxInvoiceId;
    }

    /// <summary>
    /// Sole legally-mandated DPDP Act exception to Invariant 4 snapshot immutability.
    /// Scrubs only the customer name display snapshot, never fiscal or invoice figures.
    /// </summary>
    public void ScrubCustomerNameSnapshot(string replacement)
    {
        CustomerName = replacement;
    }
}
