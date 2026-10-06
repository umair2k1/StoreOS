using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Domain.Tests.Sales;

public class EstimateConversionMarkersTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private Sale CreateTestSale(string invoiceType, string invoiceNumber)
    {
        return invoiceType == InvoiceType.TaxInvoice
            ? Sale.CreateTaxInvoice(
                _tenantId, _branchId, invoiceNumber, DateTimeOffset.UtcNow,
                null, "Customer", null, 1000m, 90m, 90m, 1180m, 0m, 1180m,
                null, null, null, null, null, null, "Test Tax Invoice", null, _userId)
            : Sale.CreateEstimate(
                _tenantId, _branchId, invoiceNumber, DateTimeOffset.UtcNow,
                null, "Customer", null, 1000m, 90m, 90m, 1180m, 0m, 0m,
                null, null, null, null, null, null, "Test Estimate", null, _userId);
    }

    [Fact]
    public void MarkConvertedFrom_OnTaxInvoice_SetsConvertedFromEstimateId()
    {
        var taxInvoice = CreateTestSale(InvoiceType.TaxInvoice, "INV-001");
        var estimateId = Guid.NewGuid();

        taxInvoice.MarkConvertedFrom(estimateId);

        taxInvoice.ConvertedFromEstimateId.Should().Be(estimateId);
    }

    [Fact]
    public void MarkConvertedToTaxInvoice_OnEstimate_SetsLinkedTaxInvoiceId()
    {
        var estimate = CreateTestSale(InvoiceType.Estimate, "EST-001");
        var taxInvoiceId = Guid.NewGuid();

        estimate.MarkConvertedToTaxInvoice(taxInvoiceId);

        estimate.LinkedTaxInvoiceId.Should().Be(taxInvoiceId);
    }

    [Fact]
    public void MarkConvertedToTaxInvoice_OnNonEstimate_ThrowsNotAnEstimate()
    {
        var taxInvoice = CreateTestSale(InvoiceType.TaxInvoice, "INV-002");
        var targetId = Guid.NewGuid();

        var act = () => taxInvoice.MarkConvertedToTaxInvoice(targetId);

        var ex = act.Should().Throw<DomainValidationException>().Which;
        ex.Code.Should().Be("not_an_estimate");
        ex.Message.Should().Contain("Only an Estimate can be marked as converted");
    }

    [Fact]
    public void MarkConvertedToTaxInvoice_CalledTwiceOnSameEstimate_ThrowsEstimateAlreadyConverted()
    {
        var estimate = CreateTestSale(InvoiceType.Estimate, "EST-002");
        var firstTaxInvoiceId = Guid.NewGuid();
        var secondTaxInvoiceId = Guid.NewGuid();

        estimate.MarkConvertedToTaxInvoice(firstTaxInvoiceId);

        var act = () => estimate.MarkConvertedToTaxInvoice(secondTaxInvoiceId);

        var ex = act.Should().Throw<DomainValidationException>().Which;
        ex.Code.Should().Be("estimate_already_converted");
        ex.Message.Should().Contain("This estimate has already been converted to a tax invoice");
    }
}
