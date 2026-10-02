using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class SettingConfiguration : IEntityTypeConfiguration<Setting>
{
    public void Configure(EntityTypeBuilder<Setting> builder)
    {
        builder.ToTable("settings");

        // Logical key for EF change tracking. Physical PK with branch_id_key is applied via raw SQL migration.
        builder.HasKey(s => new { s.TenantId, s.BranchId, s.Key });

        builder.Property(s => s.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();
        builder.Property(s => s.BranchId)
            .HasColumnName("branch_id");

        builder.Property(s => s.Key)
            .HasColumnName("key")
            .IsRequired();

        builder.Property(s => s.ValueJson)
            .HasColumnName("value")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(s => s.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
