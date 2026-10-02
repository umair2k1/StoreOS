using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence.Configurations;

public class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("user_role_assignments");

        // Logical key for EF change tracking. Physical PK with branch_id_key is applied via raw SQL migration.
        builder.HasKey(ura => new { ura.UserId, ura.RoleId, ura.BranchId });

        builder.Property(ura => ura.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(ura => ura.RoleId)
            .HasColumnName("role_id")
            .IsRequired();

        builder.Property(ura => ura.BranchId)
            .HasColumnName("branch_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(ura => ura.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<Role>()
            .WithMany()
            .HasForeignKey(ura => ura.RoleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Branch>()
            .WithMany()
            .HasForeignKey(ura => ura.BranchId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
