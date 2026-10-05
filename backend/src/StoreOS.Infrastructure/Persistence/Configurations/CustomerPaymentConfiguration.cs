using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Sales;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class CustomerPaymentConfiguration : IEntityTypeConfiguration<CustomerPayment>
{
    public void Configure(EntityTypeBuilder<CustomerPayment> builder)
    {
        builder.ToTable("customer_payments");

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(p => p.SaleId)
            .HasColumnName("sale_id")
            .IsRequired();

        builder.Property(p => p.Amount)
            .HasColumnName("amount")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(p => p.PaymentMode)
            .HasColumnName("payment_mode")
            .IsRequired();

        builder.Property(p => p.PaymentDate)
            .HasColumnName("payment_date")
            .IsRequired();

        builder.Property(p => p.ReceivedBy)
            .HasColumnName("received_by")
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.SaleId })
            .HasDatabaseName("ix_custpay_sale");

        builder.HasOne<Sale>()
            .WithMany()
            .HasForeignKey(p => p.SaleId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
