using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Domain.Tests.Sales;

public class SaleTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void CreateTaxInvoice_ShouldHaveCorrectTypeAndActiveStatus()
    {
        var sale = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-001", DateTimeOffset.UtcNow,
            customerId: null, customerName: "Rajesh", customerGstin: null,
            totalTaxable: 1000m, totalCgst: 90m, totalSgst: 90m,
            grandTotal: 1180m, discount: 0m, amountPaid: 500m,
            paymentMode: PaymentMode.UPI, financeProvider: null, financerName: null,
            downPayment: null, emiDetailsJson: null, giftDetailsJson: null,
            description: "Test Sale", repairJobId: null, createdBy: _userId);

        sale.InvoiceType.Should().Be(InvoiceType.TaxInvoice);
        sale.Status.Should().Be(SaleStatus.Active);
        sale.InvoiceNumber.Should().Be("INV-001");
        sale.AmountPaid.Should().Be(500m);
        sale.GrandTotal.Should().Be(1180m);
        sale.Items.Should().BeEmpty();
    }

    [Fact]
    public void CreateEstimate_ShouldHaveCorrectTypeAndActiveStatus()
    {
        var sale = Sale.CreateEstimate(
            _tenantId, _branchId, "EST-001", DateTimeOffset.UtcNow,
            customerId: null, customerName: "Anita", customerGstin: null,
            totalTaxable: 2000m, totalCgst: 180m, totalSgst: 180m,
            grandTotal: 2360m, discount: 0m, amountPaid: 0m,
            paymentMode: null, financeProvider: null, financerName: null,
            downPayment: null, emiDetailsJson: null, giftDetailsJson: null,
            description: "Test Estimate", repairJobId: null, createdBy: _userId);

        sale.InvoiceType.Should().Be(InvoiceType.Estimate);
        sale.Status.Should().Be(SaleStatus.Active);
        sale.InvoiceNumber.Should().Be("EST-001");
    }

    [Fact]
    public void AddItem_ShouldAccumulateItems()
    {
        var sale = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-002", DateTimeOffset.UtcNow,
            customerId: null, customerName: null, customerGstin: null,
            totalTaxable: 1000m, totalCgst: 90m, totalSgst: 90m,
            grandTotal: 1180m, discount: 0m, amountPaid: 0m,
            paymentMode: null, financeProvider: null, financerName: null,
            downPayment: null, emiDetailsJson: null, giftDetailsJson: null,
            description: null, repairJobId: null, createdBy: _userId);

        var item1 = SaleItem.Create(
            _tenantId, sale.Id, productId: Guid.NewGuid(), itemName: "Phone Case",
            category: "Accessories", qty: 2, pricePerUnit: 500m, purchasePriceSnapshot: 200m,
            hsnSnapshot: "39269099", isMarginScheme: false, isGift: false,
            taxable: 847.46m, cgst: 76.27m, sgst: 76.27m, imei: null);

        sale.AddItem(item1);

        sale.Items.Should().HaveCount(1);
        sale.Items[0].Should().BeSameAs(item1);
    }

    [Fact]
    public void MarkVoided_WhenActive_ShouldSucceed()
    {
        var sale = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-003", DateTimeOffset.UtcNow,
            customerId: null, customerName: null, customerGstin: null,
            totalTaxable: 1000m, totalCgst: 90m, totalSgst: 90m,
            grandTotal: 1180m, discount: 0m, amountPaid: 0m,
            paymentMode: null, financeProvider: null, financerName: null,
            downPayment: null, emiDetailsJson: null, giftDetailsJson: null,
            description: null, repairJobId: null, createdBy: _userId);

        sale.MarkVoided();
        sale.Status.Should().Be(SaleStatus.Voided);

        var act = () => sale.MarkVoided();
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*Cannot void sale with status 'Voided'*");
    }

    [Fact]
    public void MarkRefunded_WhenActive_ShouldSucceed()
    {
        var sale = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-004", DateTimeOffset.UtcNow,
            customerId: null, customerName: null, customerGstin: null,
            totalTaxable: 1000m, totalCgst: 90m, totalSgst: 90m,
            grandTotal: 1180m, discount: 0m, amountPaid: 0m,
            paymentMode: null, financeProvider: null, financerName: null,
            downPayment: null, emiDetailsJson: null, giftDetailsJson: null,
            description: null, repairJobId: null, createdBy: _userId);

        sale.MarkRefunded();
        sale.Status.Should().Be(SaleStatus.Refunded);

        var act = () => sale.MarkRefunded();
        act.Should().Throw<DomainValidationException>()
            .WithMessage("*Cannot refund sale with status 'Refunded'*");
    }

    [Fact]
    public void ApplyPayment_BeyondGrandTotal_ShouldThrowPaymentExceedsBalance()
    {
        var sale = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-005", DateTimeOffset.UtcNow,
            customerId: null, customerName: null, customerGstin: null,
            totalTaxable: 1000m, totalCgst: 90m, totalSgst: 90m,
            grandTotal: 1180m, discount: 0m, amountPaid: 1000m,
            paymentMode: PaymentMode.Cash, financeProvider: null, financerName: null,
            downPayment: null, emiDetailsJson: null, giftDetailsJson: null,
            description: null, repairJobId: null, createdBy: _userId);

        var act = () => sale.ApplyPayment(200m);
        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("payment_exceeds_balance");
    }

    [Fact]
    public void ApplyPayment_UpToGrandTotal_ShouldSucceed()
    {
        var sale = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-006", DateTimeOffset.UtcNow,
            customerId: null, customerName: null, customerGstin: null,
            totalTaxable: 1000m, totalCgst: 90m, totalSgst: 90m,
            grandTotal: 1180m, discount: 0m, amountPaid: 1000m,
            paymentMode: PaymentMode.Cash, financeProvider: null, financerName: null,
            downPayment: null, emiDetailsJson: null, giftDetailsJson: null,
            description: null, repairJobId: null, createdBy: _userId);

        sale.ApplyPayment(180m);
        sale.AmountPaid.Should().Be(1180m);
    }
}
