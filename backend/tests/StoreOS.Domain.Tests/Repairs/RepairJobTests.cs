using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;

namespace StoreOS.Domain.Tests.Repairs;

public class RepairJobTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    [Fact]
    public void Intake_SetsStatusReceived_AndPopulatesReceivedAt()
    {
        var before = DateTimeOffset.UtcNow;

        var job = RepairJob.Intake(
            _tenantId,
            _branchId,
            "REP-BR1-20261007-0001",
            customerId: Guid.NewGuid(),
            customerName: "Jane Doe",
            customerPhone: "9876543210",
            customerAddress: "123 High St",
            deviceBrand: "Apple",
            deviceModel: "iPhone 15 Pro",
            deviceType: "SmartPhone",
            serialNumber: "SN12345678",
            imei1: "351234567890123",
            imei2: null,
            deviceCondition: "Scratched screen",
            accessoriesReceived: "Phone case",
            problemDescription: "No display",
            customerNotes: "Urgent repair needed",
            estimatedCost: 3500m,
            advancePaid: 500m);

        var after = DateTimeOffset.UtcNow;

        job.Status.Should().Be(RepairStatus.Received);
        job.ReceivedAt.Should().NotBeNull();
        job.ReceivedAt.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
        job.JobNumber.Should().Be("REP-BR1-20261007-0001");
        job.BalanceDue.Should().Be(3000m);
    }

    [Fact]
    public void SetCommercials_CorrectlyComputesBalanceDue()
    {
        var job = RepairJob.Intake(
            _tenantId,
            _branchId,
            "REP-BR1-20261007-0002",
            customerId: null,
            customerName: "Walk-in",
            customerPhone: null,
            customerAddress: null,
            deviceBrand: "Samsung",
            deviceModel: "Galaxy S23",
            deviceType: "Phone",
            serialNumber: null,
            imei1: null,
            imei2: null,
            deviceCondition: null,
            accessoriesReceived: null,
            problemDescription: "Charging issue",
            customerNotes: null);

        job.SetCommercials(
            estimatedCost: 2000m,
            approvedCost: 2200m,
            partsCost: 1200m,
            laborCharge: 800m,
            discount: 100m,
            tax: 300m,
            finalTotal: 2200m,
            advancePaid: 500m);

        job.EstimatedCost.Should().Be(2000m);
        job.ApprovedCost.Should().Be(2200m);
        job.PartsCost.Should().Be(1200m);
        job.LaborCharge.Should().Be(800m);
        job.Discount.Should().Be(100m);
        job.Tax.Should().Be(300m);
        job.FinalTotal.Should().Be(2200m);
        job.AdvancePaid.Should().Be(500m);
        job.BalanceDue.Should().Be(1700m);
    }

    [Fact]
    public void Cancel_OnDeliveredJob_ThrowsDomainValidationException()
    {
        var job = RepairJob.Intake(
            _tenantId,
            _branchId,
            "REP-BR1-20261007-0003",
            customerId: null,
            customerName: "Customer",
            customerPhone: null,
            customerAddress: null,
            deviceBrand: "OnePlus",
            deviceModel: "11",
            deviceType: "Phone",
            serialNumber: null,
            imei1: null,
            imei2: null,
            deviceCondition: null,
            accessoriesReceived: null,
            problemDescription: "Mic issue",
            customerNotes: null);

        // Put into ReadyForPickup via reflection, then Deliver
        typeof(RepairJob).GetProperty(nameof(RepairJob.Status))!.SetValue(job, RepairStatus.ReadyForPickup);
        job.Deliver(DateTimeOffset.UtcNow, new DateOnly(2026, 11, 7));

        job.Status.Should().Be(RepairStatus.Delivered);

        var act = () => job.Cancel("Customer requested cancellation");
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*cannot cancel a delivered repair job*");
    }

    [Fact]
    public void Deliver_FromNonReadyForPickupStatus_ThrowsDomainValidationException()
    {
        var job = RepairJob.Intake(
            _tenantId,
            _branchId,
            "REP-BR1-20261007-0004",
            customerId: null,
            customerName: "Customer",
            customerPhone: null,
            customerAddress: null,
            deviceBrand: "Apple",
            deviceModel: "iPhone 14",
            deviceType: "Phone",
            serialNumber: null,
            imei1: null,
            imei2: null,
            deviceCondition: null,
            accessoriesReceived: null,
            problemDescription: "Screen crack",
            customerNotes: null);

        job.Status.Should().Be(RepairStatus.Received);

        var act = () => job.Deliver(DateTimeOffset.UtcNow, new DateOnly(2026, 11, 7));
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*Only jobs in 'ReadyForPickup' can be delivered*");
    }

    [Fact]
    public void Deliver_FromReadyForPickup_Succeeds()
    {
        var job = RepairJob.Intake(
            _tenantId,
            _branchId,
            "REP-BR1-20261007-0005",
            customerId: null,
            customerName: "Customer",
            customerPhone: null,
            customerAddress: null,
            deviceBrand: "Google",
            deviceModel: "Pixel 8",
            deviceType: "Phone",
            serialNumber: null,
            imei1: null,
            imei2: null,
            deviceCondition: null,
            accessoriesReceived: null,
            problemDescription: "Battery",
            customerNotes: null);

        typeof(RepairJob).GetProperty(nameof(RepairJob.Status))!.SetValue(job, RepairStatus.ReadyForPickup);

        var deliveryTime = DateTimeOffset.UtcNow;
        var warrantyDate = new DateOnly(2026, 12, 1);
        job.Deliver(deliveryTime, warrantyDate);

        job.Status.Should().Be(RepairStatus.Delivered);
        job.DeliveredAt.Should().Be(deliveryTime);
        job.WarrantyUntil.Should().Be(warrantyDate);
    }

    [Fact]
    public void RepairStatus_IsActive_ReturnsExpected()
    {
        RepairStatus.IsActive(RepairStatus.Received).Should().BeTrue();
        RepairStatus.IsActive(RepairStatus.Diagnosing).Should().BeTrue();
        RepairStatus.IsActive(RepairStatus.WaitingForApproval).Should().BeTrue();
        RepairStatus.IsActive(RepairStatus.WaitingForParts).Should().BeTrue();
        RepairStatus.IsActive(RepairStatus.InRepair).Should().BeTrue();
        RepairStatus.IsActive(RepairStatus.ReadyForPickup).Should().BeTrue();
        RepairStatus.IsActive(RepairStatus.Delivered).Should().BeFalse();
        RepairStatus.IsActive(RepairStatus.Cancelled).Should().BeFalse();
    }
}
