using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class InventoryImeiConfiguration : IEntityTypeConfiguration<InventoryImei>
{
    public void Configure(EntityTypeBuilder<InventoryImei> builder)
    {
        builder.ToTable("inventory_imeis");

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

        builder.Property(x => x.PurchaseId)
            .HasColumnName("purchase_id");

        builder.Property(x => x.Imei)
            .HasColumnName("imei")
            .IsRequired();

        builder.Property(x => x.Status)
            .HasColumnName("status")
            .IsRequired();

        builder.Property(x => x.SaleId)
            .HasColumnName("sale_id");

        builder.Property(x => x.SaleItemId)
            .HasColumnName("sale_item_id");

        builder.Property(x => x.ReturnId)
            .HasColumnName("return_id");

        builder.Property(x => x.PurchaseDate)
            .HasColumnName("purchase_date");

        builder.Property(x => x.PurchasePrice)
            .HasColumnName("purchase_price")
            .HasPrecision(18, 2);

        builder.Property(x => x.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(x => new { x.TenantId, x.Imei })
            .HasDatabaseName("ix_imei_lookup");

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
