using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Customers;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id");

        builder.Property(c => c.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .IsRequired();

        builder.Property(c => c.Phone)
            .HasColumnName("phone");

        builder.Property(c => c.Gstin)
            .HasColumnName("gstin");

        builder.Property(c => c.TotalPurchases)
            .HasColumnName("total_purchases")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(c => c.CreditLimit)
            .HasColumnName("credit_limit")
            .HasPrecision(18, 2);

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(c => new { c.TenantId, c.Phone })
            .HasDatabaseName("ix_customers_tenant_phone");
    }
}
