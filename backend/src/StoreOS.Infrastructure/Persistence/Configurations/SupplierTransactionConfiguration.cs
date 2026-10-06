using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class SupplierTransactionConfiguration : IEntityTypeConfiguration<SupplierTransaction>
{
    public void Configure(EntityTypeBuilder<SupplierTransaction> builder)
    {
        builder.ToTable("supplier_transactions", t =>
        {
            t.HasCheckConstraint("CK_supplier_transactions_type",
                "type IN ('Bill', 'Payment', 'OpeningBalance')");
            t.HasCheckConstraint("CK_supplier_transactions_amount",
                "amount > 0");
            t.HasCheckConstraint("CK_supplier_transactions_payment_mode",
                "payment_mode IS NULL OR payment_mode IN ('Cash', 'UPI', 'BankTransfer', 'Cheque', 'Credit')");
        });

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id");

        builder.Property(t => t.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(t => t.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(t => t.BranchId)
            .HasColumnName("branch_id");

        builder.Property(t => t.Type)
            .HasColumnName("type")
            .IsRequired();

        builder.Property(t => t.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(t => t.PaymentMode)
            .HasColumnName("payment_mode");

        builder.Property(t => t.ReferenceNo)
            .HasColumnName("reference_no");

        builder.Property(t => t.TxnDate)
            .HasColumnName("txn_date")
            .IsRequired();

        builder.Property(t => t.Notes)
            .HasColumnName("notes");

        builder.Property(t => t.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(t => new { t.TenantId, t.SupplierId })
            .HasDatabaseName("ix_suppliertxn_supplier");

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(t => t.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(t => t.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
