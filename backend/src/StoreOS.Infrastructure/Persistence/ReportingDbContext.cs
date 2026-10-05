using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Persistence;

public class ReportingDbContext : DbContext, IReportingDbContext
{
    private readonly IAccessScope _scope;

    public ReportingDbContext(DbContextOptions<ReportingDbContext> options, IAccessScope scope)
        : base(options)
    {
        _scope = scope;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreOsDbContext).Assembly);
        QueryFilters.ApplyAll(modelBuilder, () => _scope);
    }
}
