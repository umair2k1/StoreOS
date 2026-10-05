using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("sales");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id");

        builder.Property(s => s.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(s => s.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(s => s.InvoiceNumber)
            .HasColumnName("invoice_number")
            .IsRequired();

        builder.Property(s => s.InvoiceType)
            .HasColumnName("invoice_type")
            .IsRequired();

        builder.Property(s => s.SaleDate)
            .HasColumnName("sale_date")
            .IsRequired();

        builder.Property(s => s.CustomerId)
            .HasColumnName("customer_id");

        builder.Property(s => s.CustomerName)
            .HasColumnName("customer_name");

        builder.Property(s => s.CustomerGstin)
            .HasColumnName("customer_gstin");

        builder.Property(s => s.TotalTaxable)
            .HasColumnName("total_taxable")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(s => s.TotalCgst)
            .HasColumnName("total_cgst")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(s => s.TotalSgst)
            .HasColumnName("total_sgst")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(s => s.GrandTotal)
            .HasColumnName("grand_total")
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(s => s.Discount)
            .HasColumnName("discount")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(s => s.AmountPaid)
            .HasColumnName("amount_paid")
            .HasPrecision(18, 2)
            .HasDefaultValue(0m)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasDefaultValue(SaleStatus.Active)
            .IsRequired();

        builder.Property(s => s.PaymentMode)
            .HasColumnName("payment_mode");

        builder.Property(s => s.FinanceProvider)
            .HasColumnName("finance_provider");

        builder.Property(s => s.FinancerName)
            .HasColumnName("financer_name");

        builder.Property(s => s.DownPayment)
            .HasColumnName("down_payment")
            .HasPrecision(18, 2);

        builder.Property(s => s.EmiDetailsJson)
            .HasColumnName("emi_details")
            .HasColumnType("jsonb");

        builder.Property(s => s.GiftDetailsJson)
            .HasColumnName("gift_details")
            .HasColumnType("jsonb");

        builder.Property(s => s.Description)
            .HasColumnName("description");

        builder.Property(s => s.RepairJobId)
            .HasColumnName("repair_job_id");

        builder.Property(s => s.Irn)
            .HasColumnName("irn");

        builder.Property(s => s.IrnAckNo)
            .HasColumnName("irn_ack_no");

        builder.Property(s => s.IrnQrData)
            .HasColumnName("irn_qr_data");

        builder.Property(s => s.CreatedBy)
            .HasColumnName("created_by")
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(s => new { s.TenantId, s.InvoiceNumber })
            .IsUnique()
            .HasDatabaseName("ix_sales_tenant_invoice_number");

        builder.HasIndex(s => new { s.TenantId, s.BranchId, s.SaleDate })
            .HasDatabaseName("ix_sales_tenant_branch_date");

        builder.HasIndex(s => new { s.TenantId, s.Status })
            .HasDatabaseName("ix_sales_tenant_status");

        builder.HasIndex(s => new { s.TenantId, s.Irn })
            .HasFilter("irn IS NOT NULL")
            .HasDatabaseName("ix_sales_tenant_irn");

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(s => s.BranchId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(s => s.Items)
            .WithOne()
            .HasForeignKey(i => i.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(s => s.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
