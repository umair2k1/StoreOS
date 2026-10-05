using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Platform;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class AuditLogEntryConfiguration : IEntityTypeConfiguration<AuditLogEntry>
{
    public void Configure(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .UseIdentityAlwaysColumn();
        builder.Property(a => a.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(a => a.BranchId)
            .HasColumnName("branch_id");

        builder.Property(a => a.CorrelationId)
            .HasColumnName("correlation_id");

        builder.Property(a => a.OccurredAt)
            .HasColumnName("occurred_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(a => a.UserId)
            .HasColumnName("user_id");

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .IsRequired();

        builder.Property(a => a.EntityType)
            .HasColumnName("entity_type")
            .IsRequired();

        builder.Property(a => a.EntityId)
            .HasColumnName("entity_id")
            .IsRequired();

        builder.Property(a => a.ChangesJson)
            .HasColumnName("changes")
            .HasColumnType("jsonb");

        builder.HasIndex(a => new { a.TenantId, a.CorrelationId })
            .HasDatabaseName("ix_audit_log_tenant_correlation")
            .HasFilter("correlation_id IS NOT NULL");
    }
}
