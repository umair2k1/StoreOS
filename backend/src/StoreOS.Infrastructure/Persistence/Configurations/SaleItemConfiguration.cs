using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Sales;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("sale_items");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasColumnName("id");

        builder.Property(i => i.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(i => i.SaleId)
            .HasColumnName("sale_id")
            .IsRequired();

        builder.Property(i => i.ProductId)
            .HasColumnName("product_id");

        builder.Property(i => i.ItemName)
            .HasColumnName("item_name")
            .IsRequired();

        builder.Property(i => i.Category)
            .HasColumnName("category")
            .IsRequired();

        builder.Property(i => i.Qty)
            .HasColumnName("qty")
            .IsRequired();

        builder.Property(i => i.PricePerUnit)
            .HasColumnName("price_per_unit")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.PurchasePriceSnapshot)
            .HasColumnName("purchase_price_snapshot")
            .HasPrecision(18, 2);

        builder.Property(i => i.HsnSnapshot)
            .HasColumnName("hsn_snapshot");

        builder.Property(i => i.IsMarginScheme)
            .HasColumnName("is_margin_scheme")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(i => i.IsGift)
            .HasColumnName("is_gift")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(i => i.Taxable)
            .HasColumnName("taxable")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.Cgst)
            .HasColumnName("cgst")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.Sgst)
            .HasColumnName("sgst")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(i => i.Imei)
            .HasColumnName("imei");

        builder.HasIndex(i => new { i.TenantId, i.SaleId })
            .HasDatabaseName("ix_sale_items_sale");

        builder.HasIndex(i => new { i.TenantId, i.Imei })
            .HasDatabaseName("ix_sale_items_imei");

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
