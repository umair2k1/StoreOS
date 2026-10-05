using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Catalog;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("products");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(p => p.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(p => p.Category)
            .HasColumnName("category")
            .IsRequired();

        builder.Property(p => p.SellPrice)
            .HasColumnName("sell_price")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.PurchasePrice)
            .HasColumnName("purchase_price")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(p => p.GstRate)
            .HasColumnName("gst_rate")
            .HasPrecision(5, 2)
            .HasDefaultValue(18m)
            .IsRequired();

        builder.Property(p => p.IsTaxable)
            .HasColumnName("is_taxable")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.IsMarginScheme)
            .HasColumnName("is_margin_scheme")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.HsnCode)
            .HasColumnName("hsn_code");

        builder.Property(p => p.Description)
            .HasColumnName("description");

        builder.Property(p => p.EstimatedDurationMinutes)
            .HasColumnName("estimated_duration_minutes");

        builder.Property(p => p.WarrantyDays)
            .HasColumnName("warranty_days");

        builder.Property(p => p.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.Category })
            .HasDatabaseName("ix_products_tenant_cat");

        builder.HasIndex(p => new { p.TenantId, p.Name })
            .HasDatabaseName("ix_products_tenant_name");
    }
}
