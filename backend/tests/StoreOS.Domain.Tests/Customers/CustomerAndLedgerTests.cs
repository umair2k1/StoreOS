using FluentAssertions;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Tests.Customers;

public class CustomerAndLedgerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    [Fact]
    public void Customer_Create_ShouldInitializeCorrectly()
    {
        var customer = Customer.Create(_tenantId, "Aarav Sharma", "9876543210", "29AAAAA0000A1Z5");

        customer.TenantId.Should().Be(_tenantId);
        customer.Name.Should().Be("Aarav Sharma");
        customer.Phone.Should().Be("9876543210");
        customer.Gstin.Should().Be("29AAAAA0000A1Z5");
        customer.TotalPurchases.Should().Be(0m);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Customer_Create_EmptyName_ShouldThrowArgumentException(string? invalidName)
    {
        var act = () => Customer.Create(_tenantId, invalidName!, "9876543210", null);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public void Customer_UpdateProfile_ShouldUpdateFields()
    {
        var customer = Customer.Create(_tenantId, "Aarav Sharma", "9876543210", null);
        customer.UpdateProfile("Aarav S.", "9999999999", "29AAAAA0000A1Z5");

        customer.Name.Should().Be("Aarav S.");
        customer.Phone.Should().Be("9999999999");
        customer.Gstin.Should().Be("29AAAAA0000A1Z5");
    }

    [Fact]
    public void Customer_UpdateProfile_EmptyName_ShouldThrowArgumentException()
    {
        var customer = Customer.Create(_tenantId, "Aarav Sharma", "9876543210", null);
        var act = () => customer.UpdateProfile("   ", "9876543210", null);
        act.Should().Throw<ArgumentException>()
            .WithParameterName("name");
    }

    [Fact]
    public void Customer_AddToTotalPurchases_ShouldIncreaseTotalPurchases()
    {
        var customer = Customer.Create(_tenantId, "Aarav Sharma", null, null);
        customer.AddToTotalPurchases(1500m);
        customer.TotalPurchases.Should().Be(1500m);

        customer.AddToTotalPurchases(500m);
        customer.TotalPurchases.Should().Be(2000m);
    }

    [Fact]
    public void Customer_AddToTotalPurchases_NegativeAmount_ShouldThrowDomainValidationException()
    {
        var customer = Customer.Create(_tenantId, "Aarav Sharma", null, null);
        var act = () => customer.AddToTotalPurchases(-100m);
        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_amount");
    }

    [Fact]
    public void Customer_SubtractFromTotalPurchases_ShouldDecreaseTotalPurchases_NeverBelowZero()
    {
        var customer = Customer.Create(_tenantId, "Aarav Sharma", null, null);
        customer.AddToTotalPurchases(1000m);

        customer.SubtractFromTotalPurchases(400m);
        customer.TotalPurchases.Should().Be(600m);

        customer.SubtractFromTotalPurchases(1000m);
        customer.TotalPurchases.Should().Be(0m);
    }

    [Fact]
    public void Customer_SubtractFromTotalPurchases_NegativeAmount_ShouldThrowDomainValidationException()
    {
        var customer = Customer.Create(_tenantId, "Aarav Sharma", null, null);
        var act = () => customer.SubtractFromTotalPurchases(-50m);
        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_amount");
    }

    [Fact]
    public void CustomerLedgerEntry_CreateDebit_ShouldSetDebitAndZeroCredit()
    {
        var entryDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, saleId: null,
            CustomerLedgerEntryType.Invoice, 2500m, _branchId, entryDate, "INV-101");

        entry.TenantId.Should().Be(_tenantId);
        entry.CustomerId.Should().Be(_customerId);
        entry.EntryType.Should().Be(CustomerLedgerEntryType.Invoice);
        entry.Debit.Should().Be(2500m);
        entry.Credit.Should().Be(0m);
        entry.BranchId.Should().Be(_branchId);
        entry.EntryDate.Should().Be(entryDate);
        entry.RefId.Should().Be("INV-101");
    }

    [Fact]
    public void CustomerLedgerEntry_CreateCredit_ShouldSetCreditAndZeroDebit()
    {
        var entryDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = CustomerLedgerEntry.CreateCredit(
            _tenantId, _customerId, saleId: null,
            CustomerLedgerEntryType.Payment, 1500m, _branchId, entryDate, "PAY-202");

        entry.TenantId.Should().Be(_tenantId);
        entry.CustomerId.Should().Be(_customerId);
        entry.EntryType.Should().Be(CustomerLedgerEntryType.Payment);
        entry.Debit.Should().Be(0m);
        entry.Credit.Should().Be(1500m);
        entry.BranchId.Should().Be(_branchId);
        entry.EntryDate.Should().Be(entryDate);
        entry.RefId.Should().Be("PAY-202");
    }

    [Fact]
    public void CustomerLedgerEntry_InvalidEntryType_ShouldThrowDomainValidationException()
    {
        var entryDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var act = () => CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, saleId: null,
            "InvalidType", 100m, _branchId, entryDate, null);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_ledger_entry_type");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void CustomerLedgerEntry_NonPositiveAmount_ShouldThrowDomainValidationException(decimal invalidAmount)
    {
        var entryDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var actDebit = () => CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, saleId: null,
            CustomerLedgerEntryType.Invoice, invalidAmount, _branchId, entryDate, null);

        actDebit.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_amount");

        var actCredit = () => CustomerLedgerEntry.CreateCredit(
            _tenantId, _customerId, saleId: null,
            CustomerLedgerEntryType.Payment, invalidAmount, _branchId, entryDate, null);

        actCredit.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_amount");
    }
}
