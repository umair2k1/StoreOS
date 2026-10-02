using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants", t =>
        {
            t.HasCheckConstraint("ck_tenants_subdomain", "subdomain ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$'");
        });

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id");

        builder.Property(t => t.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(t => t.Subdomain)
            .HasColumnName("subdomain")
            .IsRequired();

        builder.HasIndex(t => t.Subdomain)
            .IsUnique()
            .HasDatabaseName("ux_tenants_subdomain");

        builder.Property(t => t.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(t => t.Timezone)
            .HasColumnName("timezone")
            .HasDefaultValue("Asia/Kolkata")
            .IsRequired();

        builder.Property(t => t.FeaturesJson)
            .HasColumnName("features")
            .HasColumnType("jsonb")
            .HasDefaultValue("{}")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();
    }
}
