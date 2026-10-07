using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Repairs;

public class RepairJob
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string JobNumber { get; private set; } = default!;
    public Guid? CustomerId { get; private set; }
    public string? CustomerName { get; private set; }
    public string? CustomerPhone { get; private set; }
    public string? CustomerAddress { get; private set; }
    public string? DeviceBrand { get; private set; }
    public string? DeviceModel { get; private set; }
    public string? DeviceType { get; private set; }
    public string? SerialNumber { get; private set; }
    public string? Imei1 { get; private set; }
    public string? Imei2 { get; private set; }
    public string? DeviceCondition { get; private set; }
    public string? AccessoriesReceived { get; private set; }
    public string? ProblemDescription { get; private set; }
    public string? CustomerNotes { get; private set; }
    public string? Diagnosis { get; private set; }
    public string? TechnicianNotes { get; private set; }
    public string? InternalNotes { get; private set; }
    public string? Technician { get; private set; }
    public decimal? EstimatedCost { get; private set; }
    public decimal? ApprovedCost { get; private set; }
    public decimal? PartsCost { get; private set; }
    public decimal? LaborCharge { get; private set; }
    public decimal? Discount { get; private set; }
    public decimal? Tax { get; private set; }
    public decimal? FinalTotal { get; private set; }
    public decimal? AdvancePaid { get; private set; }
    public decimal? BalanceDue { get; private set; }
    public string Status { get; private set; } = default!;
    public DateTimeOffset? ReceivedAt { get; private set; }
    public DateTimeOffset? ExpectedCompletion { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DeliveredAt { get; private set; }
    public DateOnly? WarrantyUntil { get; private set; }
    public string? ApprovalState { get; private set; }
    public DateTimeOffset? ApprovalTimestamp { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public Guid? SaleId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private RepairJob() { }

    public static RepairJob Intake(
        Guid tenantId,
        Guid branchId,
        string jobNumber,
        Guid? customerId,
        string? customerName,
        string? customerPhone,
        string? customerAddress,
        string? deviceBrand,
        string? deviceModel,
        string? deviceType,
        string? serialNumber,
        string? imei1,
        string? imei2,
        string? deviceCondition,
        string? accessoriesReceived,
        string? problemDescription,
        string? customerNotes,
        decimal? estimatedCost = null,
        decimal? advancePaid = null,
        DateTimeOffset? expectedCompletion = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Tenant ID is required.", nameof(tenantId));
        if (branchId == Guid.Empty) throw new ArgumentException("Branch ID is required.", nameof(branchId));
        if (string.IsNullOrWhiteSpace(jobNumber)) throw new ArgumentException("Job number is required.", nameof(jobNumber));

        var now = DateTimeOffset.UtcNow;
        var job = new RepairJob
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            BranchId = branchId,
            JobNumber = jobNumber.Trim(),
            CustomerId = customerId,
            CustomerName = customerName?.Trim(),
            CustomerPhone = customerPhone?.Trim(),
            CustomerAddress = customerAddress?.Trim(),
            DeviceBrand = deviceBrand?.Trim(),
            DeviceModel = deviceModel?.Trim(),
            DeviceType = deviceType?.Trim(),
            SerialNumber = serialNumber?.Trim(),
            Imei1 = imei1?.Trim(),
            Imei2 = imei2?.Trim(),
            DeviceCondition = deviceCondition?.Trim(),
            AccessoriesReceived = accessoriesReceived?.Trim(),
            ProblemDescription = problemDescription?.Trim(),
            CustomerNotes = customerNotes?.Trim(),
            EstimatedCost = estimatedCost,
            AdvancePaid = advancePaid,
            ExpectedCompletion = expectedCompletion,
            Status = RepairStatus.Received,
            ReceivedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };

        if (advancePaid.HasValue && estimatedCost.HasValue)
        {
            job.BalanceDue = estimatedCost.Value - advancePaid.Value;
        }

        return job;
    }

    public void RecordDiagnosis(string? diagnosis, string? technicianNotes, string? internalNotes, string? technician)
    {
        Diagnosis = diagnosis;
        TechnicianNotes = technicianNotes;
        InternalNotes = internalNotes;
        Technician = technician;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetCommercials(
        decimal? estimatedCost,
        decimal? approvedCost,
        decimal? partsCost,
        decimal? laborCharge,
        decimal? discount,
        decimal? tax,
        decimal? finalTotal,
        decimal? advancePaid)
    {
        EstimatedCost = estimatedCost;
        ApprovedCost = approvedCost;
        PartsCost = partsCost;
        LaborCharge = laborCharge;
        Discount = discount;
        Tax = tax;
        FinalTotal = finalTotal;
        AdvancePaid = advancePaid;

        if (finalTotal.HasValue)
        {
            BalanceDue = finalTotal.Value - (advancePaid ?? 0m);
        }
        else if (estimatedCost.HasValue)
        {
            BalanceDue = estimatedCost.Value - (advancePaid ?? 0m);
        }
        else
        {
            BalanceDue = null;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordApproval(string? approvalState, DateTimeOffset? timestamp = null)
    {
        ApprovalState = approvalState;
        ApprovalTimestamp = timestamp ?? DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void SetExpectedCompletion(DateTimeOffset? dateTime)
    {
        ExpectedCompletion = dateTime;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Cancel(string reason)
    {
        if (Status == RepairStatus.Delivered)
        {
            throw new DomainValidationException("cannot_cancel_delivered_job", "Cannot cancel a delivered repair job.");
        }

        CancellationReason = reason;
        Status = RepairStatus.Cancelled;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Deliver(DateTimeOffset deliveredAt, DateOnly? warrantyUntil)
    {
        if (Status != RepairStatus.ReadyForPickup)
        {
            throw new DomainValidationException("invalid_status_for_delivery",
                $"Cannot deliver a repair job with status '{Status}'. Only jobs in '{RepairStatus.ReadyForPickup}' can be delivered.");
        }

        DeliveredAt = deliveredAt;
        WarrantyUntil = warrantyUntil;
        Status = RepairStatus.Delivered;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void LinkToSale(Guid saleId, string invoiceNumber)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            throw new ArgumentException("Invoice number is required.", nameof(invoiceNumber));
        }

        SaleId = saleId;
        InvoiceNumber = invoiceNumber.Trim();
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
