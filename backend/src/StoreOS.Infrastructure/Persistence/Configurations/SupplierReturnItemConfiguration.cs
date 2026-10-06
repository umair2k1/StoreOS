using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class SupplierReturnItemConfiguration : IEntityTypeConfiguration<SupplierReturnItem>
{
    public void Configure(EntityTypeBuilder<SupplierReturnItem> builder)
    {
        builder.ToTable("supplier_return_items", t =>
        {
            t.HasCheckConstraint("CK_supplier_return_items_qty", "qty > 0");
        });

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasColumnName("id");

        builder.Property(i => i.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(i => i.ReturnId)
            .HasColumnName("return_id")
            .IsRequired();

        builder.Property(i => i.ProductId)
            .HasColumnName("product_id");

        builder.Property(i => i.ItemName)
            .HasColumnName("item_name")
            .IsRequired();

        builder.Property(i => i.Qty)
            .HasColumnName("qty")
            .IsRequired();

        builder.Property(i => i.PurchasePrice)
            .HasColumnName("purchase_price")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.ReturnRate)
            .HasColumnName("return_rate")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.LineTotal)
            .HasColumnName("line_total")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.ImeiNumbers)
            .HasColumnName("imei_numbers")
            .HasColumnType("text[]");

        builder.HasIndex(i => new { i.TenantId, i.ReturnId })
            .HasDatabaseName("ix_supplier_return_items_return");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
