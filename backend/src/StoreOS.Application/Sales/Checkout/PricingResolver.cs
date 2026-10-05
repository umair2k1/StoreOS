using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tax;

namespace StoreOS.Application.Sales.Checkout;

public class PricingResolver
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IInventoryStockService _stockService;

    public PricingResolver(IAppDbContext db, IAccessScope scope, IInventoryStockService stockService)
    {
        _db = db;
        _scope = scope;
        _stockService = stockService;
    }

    public async Task<ResolvedCartLine> ResolveAsync(Guid branchId, CheckoutCartLine line, CancellationToken ct)
    {
        var product = await _db.Set<Product>().FirstOrDefaultAsync(p => p.Id == line.ProductId, ct)
            ?? throw new DomainValidationException("product_not_found", $"Product {line.ProductId} not found.");
        if (!product.IsActive)
            throw new DomainValidationException("product_inactive", $"Product '{product.Name}' is not active.");

        var branchInv = await _db.Set<BranchInventory>()
            .FirstOrDefaultAsync(bi => bi.TenantId == _scope.TenantId && bi.BranchId == branchId
                && bi.ProductId == product.Id, ct);

        decimal basePrice = branchInv?.ResolveSellPrice(product.SellPrice) ?? product.SellPrice;
        decimal pricePerUnit = line.OverridePricePerUnit ?? basePrice;

        Guid? imeiRowId = null;
        decimal? costBasis = null;

        if (!string.IsNullOrEmpty(line.Imei))
        {
            var imeiSnapshot = await _stockService.LoadImeiForSaleAsync(branchId, line.Imei, ct);
            if (imeiSnapshot.ProductId != product.Id)
                throw new DomainValidationException("imei_product_mismatch",
                    $"IMEI {line.Imei} does not belong to product '{product.Name}'.");
            imeiRowId = imeiSnapshot.Id;
            costBasis = imeiSnapshot.PurchasePrice;
        }
        else if (line.UseMarginScheme)
        {
            costBasis = (branchInv?.CostPrice ?? 0m) > 0 ? branchInv!.CostPrice : product.PurchasePrice;
        }

        LineTax tax;
        if (line.UseMarginScheme)
        {
            if (product.Category != ProductCategory.UsedPhone)
                throw new DomainValidationException("margin_scheme_invalid_category",
                    "Margin scheme can only be applied to Used Phone items.");
            if (costBasis is null || costBasis <= 0)
                throw new DomainValidationException("margin_scheme_invalid_cost_basis",
                    $"Margin scheme requires a known positive cost basis for '{product.Name}'. " +
                    "Please update inventory cost before billing under margin scheme.");
            tax = TaxEngine.MarginSchemeGst(pricePerUnit * line.Qty, costBasis.Value * line.Qty, product.GstRate);
        }
        else
        {
            tax = line.IsGift ? LineTax.Zero : TaxEngine.StandardGst(pricePerUnit * line.Qty, product.GstRate);
        }

        return new ResolvedCartLine(
            product.Id,
            product.Name,
            product.Category,
            line.Qty,
            pricePerUnit,
            costBasis,
            product.HsnCode,
            product.GstRate,
            line.UseMarginScheme,
            line.IsGift,
            line.Imei,
            imeiRowId,
            tax.Taxable,
            tax.Cgst,
            tax.Sgst);
    }
}
