using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Reporting;

public record SalesMetrics(
    decimal Revenue,
    decimal BuyingCost,
    decimal Profit,
    decimal GstCollected,
    int InvoiceCount,
    int ItemsSold);

public static class ReportAggregationHelpers
{
    public static SalesMetrics ComputeSalesMetrics(
        IEnumerable<Sale> sales,
        IEnumerable<SaleItem> lines)
    {
        var salesList = sales as IReadOnlyList<Sale> ?? sales.ToList();
        var linesList = lines as IReadOnlyList<SaleItem> ?? lines.ToList();

        decimal revenue = salesList.Sum(s => s.GrandTotal);
        decimal buyingCost = linesList.Where(li => !li.IsGift).Sum(li => li.Qty * (li.PurchasePriceSnapshot ?? 0m));
        decimal profit = revenue - buyingCost;
        decimal gstCollected = linesList.Sum(li => li.Cgst + li.Sgst);
        int invoiceCount = salesList.Count;
        int itemsSold = linesList.Where(li => !li.IsGift).Sum(li => li.Qty);

        return new SalesMetrics(
            Revenue: revenue,
            BuyingCost: buyingCost,
            Profit: profit,
            GstCollected: gstCollected,
            InvoiceCount: invoiceCount,
            ItemsSold: itemsSold);
    }

    public static int CountActiveRepairs(IEnumerable<RepairJob> jobs, bool isRepairsEnabled)
    {
        if (!isRepairsEnabled) return 0;
        return jobs.Count(r => r.Status != RepairStatus.Delivered && r.Status != RepairStatus.Cancelled);
    }
}
