using Microsoft.EntityFrameworkCore;

namespace StoreOS.Application.Abstractions;

public interface IReportingDbContext
{
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
}
