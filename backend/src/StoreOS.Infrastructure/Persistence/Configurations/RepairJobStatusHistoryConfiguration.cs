using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Repairs;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class RepairJobStatusHistoryConfiguration : IEntityTypeConfiguration<RepairJobStatusHistory>
{
    public void Configure(EntityTypeBuilder<RepairJobStatusHistory> builder)
    {
        builder.ToTable("repair_job_status_history");

        builder.HasKey(h => h.Id);

        builder.Property(h => h.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();

        builder.Property(h => h.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(h => h.RepairJobId)
            .HasColumnName("repair_job_id")
            .IsRequired();

        builder.Property(h => h.FromStatus)
            .HasColumnName("from_status");

        builder.Property(h => h.ToStatus)
            .HasColumnName("to_status")
            .IsRequired();

        builder.Property(h => h.Notes)
            .HasColumnName("notes");

        builder.Property(h => h.ChangedBy)
            .HasColumnName("changed_by");

        builder.Property(h => h.ChangedAt)
            .HasColumnName("changed_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<RepairJob>()
            .WithMany()
            .HasForeignKey(h => h.RepairJobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(h => new { h.TenantId, h.RepairJobId })
            .HasDatabaseName("ix_repair_job_status_history_tenant_job");
    }
}
