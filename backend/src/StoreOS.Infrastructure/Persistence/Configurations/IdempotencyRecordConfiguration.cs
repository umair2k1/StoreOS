using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Platform;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("idempotency_keys");

        builder.HasKey(i => new { i.TenantId, i.UserId, i.Endpoint, i.Key });

        builder.Property(i => i.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(i => i.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(i => i.Key)
            .HasColumnName("key")
            .IsRequired();

        builder.Property(i => i.Endpoint)
            .HasColumnName("endpoint")
            .IsRequired();

        builder.Property(i => i.RequestHash)
            .HasColumnName("request_hash")
            .IsRequired();

        builder.Property(i => i.ResponseStatus)
            .HasColumnName("response_status");

        builder.Property(i => i.ResponseBodyJson)
            .HasColumnName("response_body")
            .HasColumnType("jsonb");

        builder.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(i => i.CreatedAt)
            .HasDatabaseName("ix_idempotency_created");
    }
}
