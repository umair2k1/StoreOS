using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class SupplierLedgerEntryConfiguration : IEntityTypeConfiguration<SupplierLedgerEntry>
{
    public void Configure(EntityTypeBuilder<SupplierLedgerEntry> builder)
    {
        builder.ToTable("supplier_ledger_entries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(e => e.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(e => e.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(e => e.BranchId)
            .HasColumnName("branch_id");

        builder.Property(e => e.EntryType)
            .HasColumnName("entry_type")
            .IsRequired();

        builder.Property(e => e.Debit)
            .HasColumnName("debit")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(e => e.Credit)
            .HasColumnName("credit")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(e => e.EntryDate)
            .HasColumnName("entry_date")
            .IsRequired();

        builder.Property(e => e.RefId)
            .HasColumnName("ref_id");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(e => new { e.TenantId, e.SupplierId })
            .HasDatabaseName("ix_suppledger_supplier");

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(e => e.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(e => e.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
