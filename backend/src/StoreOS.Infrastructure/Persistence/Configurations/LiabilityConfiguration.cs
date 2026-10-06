using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class LiabilityConfiguration : IEntityTypeConfiguration<Liability>
{
    public void Configure(EntityTypeBuilder<Liability> builder)
    {
        builder.ToTable("liabilities", t =>
        {
            t.HasCheckConstraint("CK_liabilities_type",
                "type IN ('Loan', 'CreditCard', 'Financing', 'Investment')");
        });

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id");

        builder.Property(l => l.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(l => l.Title)
            .HasColumnName("title")
            .IsRequired();

        builder.Property(l => l.Type)
            .HasColumnName("type")
            .IsRequired();

        builder.Property(l => l.LenderName)
            .HasColumnName("lender_name");

        builder.Property(l => l.TotalPrincipal)
            .HasColumnName("total_principal")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(l => l.DueDate)
            .HasColumnName("due_date");

        builder.Property(l => l.Status)
            .HasColumnName("status");

        builder.Property(l => l.Notes)
            .HasColumnName("notes");

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(l => new { l.TenantId, l.Status })
            .HasDatabaseName("ix_liabilities_tenant_status");
    }
}
