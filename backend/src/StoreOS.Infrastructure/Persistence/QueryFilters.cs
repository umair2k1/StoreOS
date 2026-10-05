using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Platform;
using StoreOS.Domain.Tenancy;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Catalog;

namespace StoreOS.Infrastructure.Persistence;

public static class QueryFilters
{
    private sealed class ScopeReplacer : ExpressionVisitor
    {
        private readonly ParameterExpression _scopeParam;
        private readonly Expression _scopeAccess;

        public ScopeReplacer(ParameterExpression scopeParam, Expression scopeAccess)
        {
            _scopeParam = scopeParam;
            _scopeAccess = scopeAccess;
        }

        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _scopeParam)
            {
                return _scopeAccess;
            }
            return base.VisitParameter(node);
        }
    }

    public static void ApplyAll(ModelBuilder modelBuilder, Expression<Func<IAccessScope>> scopeAccessor)
    {
        var scopeAccess = scopeAccessor.Body;

        ApplyFilter<Branch>(modelBuilder, scopeAccess, (b, s) => b.TenantId == s.TenantId);
        ApplyFilter<Setting>(modelBuilder, scopeAccess, (s, scope) => s.TenantId == scope.TenantId);
        ApplyFilter<AuditLogEntry>(modelBuilder, scopeAccess, (a, s) => a.TenantId == s.TenantId && (a.BranchId == null || s.AllBranches || s.BranchIds.Contains(a.BranchId.Value)));
        ApplyFilter<IdempotencyRecord>(modelBuilder, scopeAccess, (i, s) => i.TenantId == s.TenantId);
        ApplyFilter<Product>(modelBuilder, scopeAccess, (p, s) => p.TenantId == s.TenantId);
        ApplyFilter<BranchInventory>(modelBuilder, scopeAccess, (bi, s) => bi.TenantId == s.TenantId && (s.AllBranches || s.BranchIds.Contains(bi.BranchId)));
        ApplyFilter<InventoryMovement>(modelBuilder, scopeAccess, (im, s) => im.TenantId == s.TenantId && (s.AllBranches || s.BranchIds.Contains(im.BranchId)));
    }

    private static void ApplyFilter<TEntity>(
        ModelBuilder modelBuilder,
        Expression scopeAccess,
        Expression<Func<TEntity, IAccessScope, bool>> template)
        where TEntity : class
    {
        var entityParam = template.Parameters[0];
        var scopeParam = template.Parameters[1];

        var replacer = new ScopeReplacer(scopeParam, scopeAccess);
        var newBody = replacer.Visit(template.Body);

        var lambda = Expression.Lambda<Func<TEntity, bool>>(newBody, entityParam);
        modelBuilder.Entity<TEntity>().HasQueryFilter(lambda);
    }

    public static void ApplyAll(ModelBuilder modelBuilder, IAccessScope scope)
    {
        ApplyAll(modelBuilder, () => scope);
    }
}
