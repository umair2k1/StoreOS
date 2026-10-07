using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Repairs;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class RepairJobPartConfiguration : IEntityTypeConfiguration<RepairJobPart>
{
    public void Configure(EntityTypeBuilder<RepairJobPart> builder)
    {
        builder.ToTable("repair_job_parts", t =>
        {
            t.HasCheckConstraint("CK_repair_job_parts_quantity", "quantity > 0");
            t.HasCheckConstraint("CK_repair_job_parts_status", "status IN ('Reserved','Consumed','Returned')");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasColumnName("id");
        builder.Property(p => p.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(p => p.RepairJobId).HasColumnName("repair_job_id").IsRequired();
        builder.Property(p => p.ProductId).HasColumnName("product_id");
        builder.Property(p => p.PartName).HasColumnName("part_name").IsRequired();
        builder.Property(p => p.Quantity).HasColumnName("quantity").IsRequired();
        builder.Property(p => p.UnitCost).HasColumnName("unit_cost").HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.ChargeAmount).HasColumnName("charge_amount").HasPrecision(18, 2).IsRequired();
        builder.Property(p => p.Status).HasColumnName("status").IsRequired();
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasOne<RepairJob>()
            .WithMany()
            .HasForeignKey(p => p.RepairJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(p => new { p.TenantId, p.RepairJobId })
            .HasDatabaseName("ix_repair_job_parts_tenant_job");

        builder.HasIndex(p => new { p.TenantId, p.ProductId })
            .HasDatabaseName("ix_repair_job_parts_tenant_product");
    }
}
