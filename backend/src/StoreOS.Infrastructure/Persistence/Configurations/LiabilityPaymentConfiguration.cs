using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class LiabilityPaymentConfiguration : IEntityTypeConfiguration<LiabilityPayment>
{
    public void Configure(EntityTypeBuilder<LiabilityPayment> builder)
    {
        builder.ToTable("liability_payments", t =>
        {
            t.HasCheckConstraint("CK_liability_payments_amount", "amount > 0");
        });

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasColumnName("id");

        builder.Property(p => p.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(p => p.LiabilityId)
            .HasColumnName("liability_id")
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

        builder.Property(p => p.Notes)
            .HasColumnName("notes");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(p => new { p.TenantId, p.LiabilityId })
            .HasDatabaseName("ix_liability_payments_liability");

        builder.HasOne<Liability>()
            .WithMany()
            .HasForeignKey(p => p.LiabilityId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
