using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class InvoiceSequenceConfiguration : IEntityTypeConfiguration<InvoiceSequence>
{
    public void Configure(EntityTypeBuilder<InvoiceSequence> builder)
    {
        builder.ToTable("invoice_sequences");

        builder.HasKey(s => new { s.TenantId, s.BranchId, s.SeriesKey });

        builder.Property(s => s.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(s => s.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(s => s.SeriesKey)
            .HasColumnName("series_key")
            .IsRequired();

        builder.Property(s => s.LastSeq)
            .HasColumnName("last_seq")
            .HasDefaultValue(0)
            .IsRequired();

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(s => s.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
