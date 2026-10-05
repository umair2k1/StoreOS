using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movements");

        builder.HasKey(im => im.Id);

        builder.Property(im => im.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(im => im.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(im => im.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(im => im.ProductId)
            .HasColumnName("product_id")
            .IsRequired();

        builder.Property(im => im.ImeiId)
            .HasColumnName("imei_id");

        builder.Property(im => im.MovementType)
            .HasColumnName("movement_type")
            .IsRequired();

        builder.Property(im => im.QtyDelta)
            .HasColumnName("qty_delta")
            .IsRequired();

        builder.Property(im => im.UnitCost)
            .HasColumnName("unit_cost")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(im => im.RefType)
            .HasColumnName("ref_type")
            .IsRequired();

        builder.Property(im => im.RefId)
            .HasColumnName("ref_id")
            .IsRequired();

        builder.Property(im => im.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(im => im.CreatedBy)
            .HasColumnName("created_by");

        builder.HasIndex(im => new { im.TenantId, im.BranchId, im.ProductId })
            .HasDatabaseName("ix_movements_tenant_branch_product");

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(im => im.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Product>()
            .WithMany()
            .HasForeignKey(im => im.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
