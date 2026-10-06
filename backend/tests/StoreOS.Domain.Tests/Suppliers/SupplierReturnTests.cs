using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Domain.Tests.Suppliers;

public class SupplierReturnTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _supplierId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void SupplierReturn_Create_ValidWithMultipleLineItems_ShouldAccumulateCorrectly()
    {
        var ret = SupplierReturn.Create(
            _tenantId,
            _branchId,
            "RET-MAIN-20261007-001",
            new DateOnly(2026, 10, 7),
            _supplierId,
            "9876543210",
            "Bangalore",
            SettlementType.PayableDeduction,
            "Damaged goods",
            "Return to vendor notes",
            _userId);

        ret.TotalQty.Should().Be(0);
        ret.TotalAmount.Should().Be(0m);
        ret.Items.Should().BeEmpty();

        var item1 = ret.AddItem(
            productId: Guid.NewGuid(),
            itemName: "Defective USB Cable",
            qty: 2,
            purchasePrice: 1000m,
            returnRate: 950m,
            imeiNumbers: null);

        item1.LineTotal.Should().Be(1900m);
        ret.TotalQty.Should().Be(2);
        ret.TotalAmount.Should().Be(1900m);

        var item2 = ret.AddItem(
            productId: Guid.NewGuid(),
            itemName: "Scratched Screen Protector",
            qty: 3,
            purchasePrice: 500m,
            returnRate: 500m,
            imeiNumbers: new[] { "IMEI001", "IMEI002", "IMEI003" });

        item2.LineTotal.Should().Be(1500m);
        ret.TotalQty.Should().Be(5);
        ret.TotalAmount.Should().Be(3400m);
        ret.Items.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("InvalidSettlement")]
    [InlineData("BankTransfer")]
    [InlineData("")]
    public void SupplierReturn_Create_InvalidSettlementType_ShouldThrowDomainValidationException(string invalidSettlement)
    {
        Action act = () => SupplierReturn.Create(
            _tenantId,
            _branchId,
            "RET-MAIN-20261007-001",
            new DateOnly(2026, 10, 7),
            _supplierId,
            null,
            null,
            invalidSettlement,
            null,
            null,
            _userId);

        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "invalid_settlement_type");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void SupplierReturnItem_Create_ZeroOrNegativeQuantity_ShouldThrowDomainValidationException(int invalidQty)
    {
        Action act = () => SupplierReturnItem.Create(
            _tenantId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "Faulty Adapter",
            invalidQty,
            200m,
            200m);

        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "invalid_qty");
    }

    [Fact]
    public void SupplierReturn_Create_EmptyReturnNumber_ShouldThrowDomainValidationException()
    {
        Action act = () => SupplierReturn.Create(
            _tenantId,
            _branchId,
            "",
            new DateOnly(2026, 10, 7),
            _supplierId,
            null,
            null,
            SettlementType.CashRefund,
            null,
            null,
            _userId);

        act.Should().Throw<DomainValidationException>()
            .Where(ex => ex.Code == "invalid_return_number");
    }
}
