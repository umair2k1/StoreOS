using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Platform;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Tenancy;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Customers;

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
        ApplyFilter<InventoryImei>(modelBuilder, scopeAccess, (ii, s) => ii.TenantId == s.TenantId && (s.AllBranches || s.BranchIds.Contains(ii.BranchId) || ii.Status == "InTransit"));
        ApplyFilter<Purchase>(modelBuilder, scopeAccess, (p, s) => p.TenantId == s.TenantId && (s.AllBranches || s.BranchIds.Contains(p.BranchId)));
        ApplyFilter<Supplier>(modelBuilder, scopeAccess, (su, s) => su.TenantId == s.TenantId);
        ApplyFilter<ImportBatch>(modelBuilder, scopeAccess, (ib, s) => ib.TenantId == s.TenantId && (ib.BranchId == null || s.AllBranches || s.BranchIds.Contains(ib.BranchId.Value)));
        ApplyFilter<InventoryTransfer>(modelBuilder, scopeAccess, (it, s) => it.TenantId == s.TenantId && (s.AllBranches || s.BranchIds.Contains(it.FromBranchId) || s.BranchIds.Contains(it.ToBranchId)));
        ApplyFilter<Sale>(modelBuilder, scopeAccess, (sale, s) => sale.TenantId == s.TenantId && (s.AllBranches || s.BranchIds.Contains(sale.BranchId)));
        ApplyFilter<SaleItem>(modelBuilder, scopeAccess, (item, s) => item.TenantId == s.TenantId);
        ApplyFilter<Customer>(modelBuilder, scopeAccess, (c, s) => c.TenantId == s.TenantId);
        ApplyFilter<CustomerLedgerEntry>(modelBuilder, scopeAccess, (cle, s) => cle.TenantId == s.TenantId && (cle.BranchId == null || s.AllBranches || s.BranchIds.Contains(cle.BranchId.Value)));
        ApplyFilter<CustomerPayment>(modelBuilder, scopeAccess, (cp, s) => cp.TenantId == s.TenantId);
        ApplyFilter<SupplierTransaction>(modelBuilder, scopeAccess, (st, s) => st.TenantId == s.TenantId && (st.BranchId == null || s.AllBranches || s.BranchIds.Contains(st.BranchId.Value)));
        ApplyFilter<SupplierLedgerEntry>(modelBuilder, scopeAccess, (sle, s) => sle.TenantId == s.TenantId && (sle.BranchId == null || s.AllBranches || s.BranchIds.Contains(sle.BranchId.Value)));
        ApplyFilter<SupplierReturn>(modelBuilder, scopeAccess, (sr, s) => sr.TenantId == s.TenantId && (s.AllBranches || s.BranchIds.Contains(sr.BranchId)));
        ApplyFilter<SupplierReturnItem>(modelBuilder, scopeAccess, (sri, s) => sri.TenantId == s.TenantId);
        ApplyFilter<StoreOS.Domain.FinanceOps.Expense>(modelBuilder, scopeAccess, (e, s) => e.TenantId == s.TenantId && (e.BranchId == null || s.AllBranches || s.BranchIds.Contains(e.BranchId.Value)));
        ApplyFilter<StoreOS.Domain.FinanceOps.Liability>(modelBuilder, scopeAccess, (l, s) => l.TenantId == s.TenantId);
        ApplyFilter<StoreOS.Domain.FinanceOps.LiabilityPayment>(modelBuilder, scopeAccess, (lp, s) => lp.TenantId == s.TenantId);
        ApplyFilter<StoreOS.Domain.Repairs.RepairJob>(modelBuilder, scopeAccess, (rj, s) => rj.TenantId == s.TenantId && (s.AllBranches || s.BranchIds.Contains(rj.BranchId)));
        ApplyFilter<StoreOS.Domain.Repairs.RepairJobStatusHistory>(modelBuilder, scopeAccess, (h, s) => h.TenantId == s.TenantId);
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
