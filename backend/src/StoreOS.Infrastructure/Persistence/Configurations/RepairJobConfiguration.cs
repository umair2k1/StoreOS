using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Repairs;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class RepairJobConfiguration : IEntityTypeConfiguration<RepairJob>
{
    public void Configure(EntityTypeBuilder<RepairJob> builder)
    {
        builder.ToTable("repair_jobs", t =>
        {
            t.HasCheckConstraint("CK_repair_jobs_status",
                "status IN ('Received','Diagnosing','WaitingForApproval','WaitingForParts','InRepair','ReadyForPickup','Delivered','Cancelled')");
        });

        builder.HasKey(j => j.Id);

        builder.Property(j => j.Id).HasColumnName("id");
        builder.Property(j => j.TenantId).HasColumnName("tenant_id").IsRequired();
        builder.Property(j => j.BranchId).HasColumnName("branch_id").IsRequired();
        builder.Property(j => j.JobNumber).HasColumnName("job_number").IsRequired();
        builder.Property(j => j.CustomerId).HasColumnName("customer_id");
        builder.Property(j => j.CustomerName).HasColumnName("customer_name");
        builder.Property(j => j.CustomerPhone).HasColumnName("customer_phone");
        builder.Property(j => j.CustomerAddress).HasColumnName("customer_address");
        builder.Property(j => j.DeviceBrand).HasColumnName("device_brand");
        builder.Property(j => j.DeviceModel).HasColumnName("device_model");
        builder.Property(j => j.DeviceType).HasColumnName("device_type");
        builder.Property(j => j.SerialNumber).HasColumnName("serial_number");
        builder.Property(j => j.Imei1).HasColumnName("imei1");
        builder.Property(j => j.Imei2).HasColumnName("imei2");
        builder.Property(j => j.DeviceCondition).HasColumnName("device_condition");
        builder.Property(j => j.AccessoriesReceived).HasColumnName("accessories_received");
        builder.Property(j => j.ProblemDescription).HasColumnName("problem_description");
        builder.Property(j => j.CustomerNotes).HasColumnName("customer_notes");
        builder.Property(j => j.Diagnosis).HasColumnName("diagnosis");
        builder.Property(j => j.TechnicianNotes).HasColumnName("technician_notes");
        builder.Property(j => j.InternalNotes).HasColumnName("internal_notes");
        builder.Property(j => j.Technician).HasColumnName("technician");

        builder.Property(j => j.EstimatedCost).HasColumnName("estimated_cost").HasPrecision(18, 2);
        builder.Property(j => j.ApprovedCost).HasColumnName("approved_cost").HasPrecision(18, 2);
        builder.Property(j => j.PartsCost).HasColumnName("parts_cost").HasPrecision(18, 2);
        builder.Property(j => j.LaborCharge).HasColumnName("labor_charge").HasPrecision(18, 2);
        builder.Property(j => j.Discount).HasColumnName("discount").HasPrecision(18, 2);
        builder.Property(j => j.Tax).HasColumnName("tax").HasPrecision(18, 2);
        builder.Property(j => j.FinalTotal).HasColumnName("final_total").HasPrecision(18, 2);
        builder.Property(j => j.AdvancePaid).HasColumnName("advance_paid").HasPrecision(18, 2);
        builder.Property(j => j.BalanceDue).HasColumnName("balance_due").HasPrecision(18, 2);

        builder.Property(j => j.Status).HasColumnName("status").IsRequired();
        builder.Property(j => j.ReceivedAt).HasColumnName("received_at");
        builder.Property(j => j.ExpectedCompletion).HasColumnName("expected_completion");
        builder.Property(j => j.CompletedAt).HasColumnName("completed_at");
        builder.Property(j => j.DeliveredAt).HasColumnName("delivered_at");
        builder.Property(j => j.WarrantyUntil).HasColumnName("warranty_until");
        builder.Property(j => j.ApprovalState).HasColumnName("approval_state");
        builder.Property(j => j.ApprovalTimestamp).HasColumnName("approval_timestamp");
        builder.Property(j => j.CancellationReason).HasColumnName("cancellation_reason");
        builder.Property(j => j.InvoiceNumber).HasColumnName("invoice_number");
        builder.Property(j => j.SaleId).HasColumnName("sale_id");
        builder.Property(j => j.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();
        builder.Property(j => j.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(j => new { j.TenantId, j.JobNumber })
            .IsUnique()
            .HasDatabaseName("ix_repair_jobs_tenant_job_number");

        builder.HasIndex(j => new { j.TenantId, j.BranchId, j.Status })
            .HasDatabaseName("ix_repair_jobs_tenant_branch_status");

        builder.HasIndex(j => new { j.TenantId, j.BranchId, j.CustomerId })
            .HasDatabaseName("ix_repair_jobs_tenant_branch_customer");
    }
}
