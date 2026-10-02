using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class BranchConfiguration : IEntityTypeConfiguration<Branch>
{
    public void Configure(EntityTypeBuilder<Branch> builder)
    {
        builder.ToTable("branches", t =>
        {
            t.HasCheckConstraint("ck_branches_code", "code ~ '^[A-Z1-9][A-Z0-9]{0,2}$'");
        });

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id");

        builder.Property(b => b.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(b => b.Code)
            .HasColumnName("code")
            .IsRequired();

        builder.Property(b => b.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(b => b.Address)
            .HasColumnName("address");

        builder.Property(b => b.Phone)
            .HasColumnName("phone");

        builder.Property(b => b.Email)
            .HasColumnName("email");

        builder.Property(b => b.Gstin)
            .HasColumnName("gstin");

        builder.Property(b => b.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(b => b.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(b => new { b.TenantId, b.Code })
            .IsUnique();

        builder.HasOne<Tenant>()
            .WithMany()
            .HasForeignKey(b => b.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
