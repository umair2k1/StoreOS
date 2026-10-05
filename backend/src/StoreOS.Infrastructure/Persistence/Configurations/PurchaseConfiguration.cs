using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class PurchaseConfiguration : IEntityTypeConfiguration<Purchase>
{
    public void Configure(EntityTypeBuilder<Purchase> builder)
    {
        builder.ToTable("purchases");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasColumnName("id");

        builder.Property(x => x.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(x => x.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(x => x.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(x => x.Qty)
            .HasColumnName("qty")
            .IsRequired();

        builder.Property(x => x.PurchaseRate)
            .HasColumnName("purchase_rate")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(x => x.SupplierId)
            .HasColumnName("supplier_id");

        builder.Property(x => x.SupplierName)
            .HasColumnName("supplier_name");

        builder.Property(x => x.SupplierPhone)
            .HasColumnName("supplier_phone");

        builder.Property(x => x.SupplierAddress)
            .HasColumnName("supplier_address");

        builder.Property(x => x.SellerName)
            .HasColumnName("seller_name");

        builder.Property(x => x.SellerPhone)
            .HasColumnName("seller_phone");

        builder.Property(x => x.SellerAddress)
            .HasColumnName("seller_address");

        builder.Property(x => x.SupplierInvoiceNo)
            .HasColumnName("supplier_invoice_no");

        builder.Property(x => x.ImportBatchId)
            .HasColumnName("import_batch_id");

        builder.Property(x => x.PurchaseDate)
            .HasColumnName("purchase_date")
            .IsRequired();

        builder.Property(x => x.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.ProductId })
            .HasDatabaseName("ix_purchases_tenant_branch_product");

        builder.HasIndex(x => new { x.TenantId, x.BranchId, x.PurchaseDate })
            .HasDatabaseName("ix_purchases_tenant_branch_date");

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(x => x.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
