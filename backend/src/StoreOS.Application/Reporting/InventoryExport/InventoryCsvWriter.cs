using System.Globalization;
using System.Text;

namespace StoreOS.Application.Reporting.InventoryExport;

public static class InventoryCsvWriter
{
    public static string ToCsv(InventoryExportDto export, bool includePurchasePrice = true)
    {
        var sb = new StringBuilder();

        // Section 1: Physical items
        if (export.PhysicalItems.Count > 0 || export.Services.Count == 0)
        {
            sb.AppendLine("Item,Category,Qty,PurchasePrice,SellPrice,GstRate,Hsn,Supplier,ImeiCount");
            foreach (var item in export.PhysicalItems)
            {
                var priceStr = includePurchasePrice ? FormatDecimal(item.PurchasePrice) : "0.00";
                sb.AppendLine(string.Join(",",
                    Escape(item.Item),
                    Escape(item.Category),
                    item.Quantity.ToString(CultureInfo.InvariantCulture),
                    priceStr,
                    FormatDecimal(item.SellingPrice),
                    FormatDecimal(item.Gst),
                    Escape(item.Hsn ?? ""),
                    Escape(item.Supplier ?? ""),
                    Escape(item.ImeiSummary ?? "")));
            }
        }

        // Section 2: Services
        if (export.Services.Count > 0)
        {
            if (export.PhysicalItems.Count > 0)
            {
                sb.AppendLine();
            }
            sb.AppendLine("Service,Description,ServiceCharge,Taxable,GstRate,Sac");
            foreach (var s in export.Services)
            {
                sb.AppendLine(string.Join(",",
                    Escape(s.Service),
                    Escape(s.Description ?? ""),
                    FormatDecimal(s.ServiceCharge),
                    s.Taxable ? "true" : "false",
                    FormatDecimal(s.Gst),
                    Escape(s.Sac ?? "")));
            }
        }

        return sb.ToString();
    }

    public static string Escape(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";

        bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\r') || value.Contains('\n');
        if (!mustQuote) return value;

        return $"\"{value.Replace("\"", "\"\"")}\"";
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.00", CultureInfo.InvariantCulture);
    }
}
