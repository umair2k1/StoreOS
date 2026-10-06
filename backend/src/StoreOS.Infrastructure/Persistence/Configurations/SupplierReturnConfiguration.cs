using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class SupplierReturnConfiguration : IEntityTypeConfiguration<SupplierReturn>
{
    public void Configure(EntityTypeBuilder<SupplierReturn> builder)
    {
        builder.ToTable("supplier_returns", t =>
        {
            t.HasCheckConstraint("CK_supplier_returns_settlement_type",
                "settlement_type IN ('PayableDeduction', 'CashRefund', 'PendingCredit')");
        });

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id");

        builder.Property(r => r.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(r => r.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(r => r.ReturnNumber)
            .HasColumnName("return_number")
            .IsRequired();

        builder.Property(r => r.ReturnDate)
            .HasColumnName("return_date")
            .IsRequired();

        builder.Property(r => r.SupplierId)
            .HasColumnName("supplier_id")
            .IsRequired();

        builder.Property(r => r.SupplierPhone)
            .HasColumnName("supplier_phone");

        builder.Property(r => r.SupplierAddress)
            .HasColumnName("supplier_address");

        builder.Property(r => r.TotalQty)
            .HasColumnName("total_qty")
            .IsRequired();

        builder.Property(r => r.TotalAmount)
            .HasColumnName("total_amount")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.SettlementType)
            .HasColumnName("settlement_type")
            .IsRequired();

        builder.Property(r => r.Reason)
            .HasColumnName("reason");

        builder.Property(r => r.Notes)
            .HasColumnName("notes");

        builder.Property(r => r.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(r => new { r.TenantId, r.ReturnNumber })
            .IsUnique()
            .HasDatabaseName("uq_supplier_returns_tenant_return_number");

        builder.HasIndex(r => new { r.TenantId, r.BranchId, r.ReturnDate })
            .HasDatabaseName("ix_supplier_returns_tenant_branch_date");

        builder.HasIndex(r => new { r.TenantId, r.SupplierId })
            .HasDatabaseName("ix_supplier_returns_tenant_supplier");

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(r => r.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Supplier>()
            .WithMany()
            .HasForeignKey(r => r.SupplierId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(r => r.Items)
            .WithOne()
            .HasForeignKey(i => i.ReturnId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(r => r.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
