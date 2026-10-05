using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Identity;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(rt => rt.Id);
        builder.Property(rt => rt.Id).HasColumnName("id");

        builder.Property(rt => rt.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(rt => rt.TokenHash)
            .HasColumnName("token_hash")
            .IsRequired();

        builder.Property(rt => rt.FamilyId)
            .HasColumnName("family_id")
            .IsRequired();

        builder.Property(rt => rt.IsRevoked)
            .HasColumnName("is_revoked")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(rt => rt.ReplacedByTokenId)
            .HasColumnName("replaced_by_token_id");

        builder.Property(rt => rt.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(rt => rt.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(rt => rt.UserId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasOne<RefreshToken>()
            .WithMany()
            .HasForeignKey(rt => rt.ReplacedByTokenId)
            .OnDelete(DeleteBehavior.NoAction);

        builder.HasIndex(rt => rt.FamilyId)
            .HasDatabaseName("ix_refresh_tokens_family");

        builder.HasIndex(rt => new { rt.UserId, rt.TokenHash })
            .HasDatabaseName("ix_refresh_tokens_lookup");
    }
}
