using FluentAssertions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Domain.Tests.Suppliers;

public class SupplierTransactionAndLedgerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _supplierId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public void SupplierTransaction_Create_Bill_ShouldSucceed()
    {
        var txnDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var txn = SupplierTransaction.Create(
            _tenantId, _supplierId, _branchId, SupplierTransactionType.Bill, 50000m,
            null, "BILL-001", txnDate, "Monthly supply", _userId);

        txn.TenantId.Should().Be(_tenantId);
        txn.SupplierId.Should().Be(_supplierId);
        txn.BranchId.Should().Be(_branchId);
        txn.Type.Should().Be(SupplierTransactionType.Bill);
        txn.Amount.Should().Be(50000m);
        txn.PaymentMode.Should().BeNull();
        txn.ReferenceNo.Should().Be("BILL-001");
        txn.TxnDate.Should().Be(txnDate);
        txn.Notes.Should().Be("Monthly supply");
        txn.CreatedBy.Should().Be(_userId);
    }

    [Fact]
    public void SupplierTransaction_Create_OpeningBalance_ShouldSucceed()
    {
        var txnDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var txn = SupplierTransaction.Create(
            _tenantId, _supplierId, null, SupplierTransactionType.OpeningBalance, 25000m,
            null, "OB-01", txnDate, "Opening balance", _userId);

        txn.Type.Should().Be(SupplierTransactionType.OpeningBalance);
        txn.Amount.Should().Be(25000m);
        txn.BranchId.Should().BeNull();
    }

    [Fact]
    public void SupplierTransaction_Create_Payment_WithValidMode_ShouldSucceed()
    {
        var txnDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var txn = SupplierTransaction.Create(
            _tenantId, _supplierId, _branchId, SupplierTransactionType.Payment, 30000m,
            SupplierPaymentMode.BankTransfer, "NEFT-123", txnDate, "Vendor payout", _userId);

        txn.Type.Should().Be(SupplierTransactionType.Payment);
        txn.Amount.Should().Be(30000m);
        txn.PaymentMode.Should().Be(SupplierPaymentMode.BankTransfer);
    }

    [Fact]
    public void SupplierTransaction_Create_Payment_WithoutPaymentMode_ShouldThrowDomainValidationException()
    {
        var txnDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var act = () => SupplierTransaction.Create(
            _tenantId, _supplierId, _branchId, SupplierTransactionType.Payment, 10000m,
            null, "REF-01", txnDate, null, _userId);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_payment_mode");
    }

    [Fact]
    public void SupplierTransaction_Create_Payment_WithInvalidMode_ShouldThrowDomainValidationException()
    {
        var txnDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var act = () => SupplierTransaction.Create(
            _tenantId, _supplierId, _branchId, SupplierTransactionType.Payment, 10000m,
            "Bitcoin", "REF-01", txnDate, null, _userId);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_payment_mode");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void SupplierTransaction_Create_NonPositiveAmount_ShouldThrowDomainValidationException(decimal invalidAmount)
    {
        var txnDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var act = () => SupplierTransaction.Create(
            _tenantId, _supplierId, _branchId, SupplierTransactionType.Bill, invalidAmount,
            null, "BILL-001", txnDate, null, _userId);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_amount");
    }

    [Fact]
    public void SupplierTransaction_Create_InvalidType_ShouldThrowDomainValidationException()
    {
        var txnDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var act = () => SupplierTransaction.Create(
            _tenantId, _supplierId, _branchId, "RandomType", 5000m,
            null, "REF-01", txnDate, null, _userId);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_transaction_type");
    }

    [Fact]
    public void SupplierLedgerEntry_CreateDebit_ShouldSetDebitAndZeroCredit()
    {
        var entryDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = SupplierLedgerEntry.CreateDebit(
            _tenantId, _supplierId, _branchId,
            SupplierLedgerEntryType.Payment, 15000m, entryDate, "PAY-001");

        entry.TenantId.Should().Be(_tenantId);
        entry.SupplierId.Should().Be(_supplierId);
        entry.BranchId.Should().Be(_branchId);
        entry.EntryType.Should().Be(SupplierLedgerEntryType.Payment);
        entry.Debit.Should().Be(15000m);
        entry.Credit.Should().Be(0m);
        entry.EntryDate.Should().Be(entryDate);
        entry.RefId.Should().Be("PAY-001");
    }

    [Fact]
    public void SupplierLedgerEntry_CreateCredit_ShouldSetCreditAndZeroDebit()
    {
        var entryDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var entry = SupplierLedgerEntry.CreateCredit(
            _tenantId, _supplierId, _branchId,
            SupplierLedgerEntryType.Bill, 25000m, entryDate, "BILL-001");

        entry.TenantId.Should().Be(_tenantId);
        entry.SupplierId.Should().Be(_supplierId);
        entry.BranchId.Should().Be(_branchId);
        entry.EntryType.Should().Be(SupplierLedgerEntryType.Bill);
        entry.Debit.Should().Be(0m);
        entry.Credit.Should().Be(25000m);
        entry.EntryDate.Should().Be(entryDate);
        entry.RefId.Should().Be("BILL-001");
    }

    [Fact]
    public void SupplierLedgerEntry_InvalidEntryType_ShouldThrowDomainValidationException()
    {
        var entryDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var act = () => SupplierLedgerEntry.CreateCredit(
            _tenantId, _supplierId, _branchId,
            "InvalidType", 1000m, entryDate, null);

        act.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_ledger_entry_type");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void SupplierLedgerEntry_NonPositiveAmount_ShouldThrowDomainValidationException(decimal invalidAmount)
    {
        var entryDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var actDebit = () => SupplierLedgerEntry.CreateDebit(
            _tenantId, _supplierId, _branchId,
            SupplierLedgerEntryType.Payment, invalidAmount, entryDate, null);

        actDebit.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_amount");

        var actCredit = () => SupplierLedgerEntry.CreateCredit(
            _tenantId, _supplierId, _branchId,
            SupplierLedgerEntryType.Bill, invalidAmount, entryDate, null);

        actCredit.Should().Throw<DomainValidationException>()
            .Which.Code.Should().Be("invalid_amount");
    }
}
