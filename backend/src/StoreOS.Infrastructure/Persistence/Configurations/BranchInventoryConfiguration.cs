using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class BranchInventoryConfiguration : IEntityTypeConfiguration<BranchInventory>
{
    public void Configure(EntityTypeBuilder<BranchInventory> builder)
    {
        builder.ToTable("branch_inventory");

        builder.HasKey(bi => bi.Id);

        builder.Property(bi => bi.Id)
            .HasColumnName("id");

        builder.Property(bi => bi.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(bi => bi.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(bi => bi.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(bi => bi.StockQty)
            .HasColumnName("stock_qty")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(bi => bi.CostPrice)
            .HasColumnName("cost_price")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(bi => bi.SellPriceOverride)
            .HasColumnName("sell_price_override")
            .HasPrecision(18, 2);

        builder.Property(bi => bi.LowStockThreshold)
            .HasColumnName("low_stock_threshold")
            .HasDefaultValue(3)
            .IsRequired();

        builder.HasIndex(bi => new { bi.TenantId, bi.BranchId, bi.ProductId })
            .IsUnique()
            .HasDatabaseName("ix_branch_inventory_tenant_branch_product");

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(bi => bi.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(bi => bi.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
