using System.Globalization;
using System.Text;

namespace StoreOS.Application.Reporting.Gstr1Export;

public static class Gstr1CsvWriter
{
    public static string ToCsv(Gstr1ExportDto export)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Gstin,InvoiceNumber,Date,CustomerName,CustomerGstin,Hsn,Category,TaxableValue,Cgst,Sgst,TotalValue");

        foreach (var group in export.ByGstin)
        {
            foreach (var row in group.Rows)
            {
                sb.AppendLine(string.Join(",",
                    Escape(group.Gstin),
                    Escape(row.InvoiceNumber),
                    row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Escape(row.CustomerName),
                    Escape(row.CustomerGstin ?? ""),
                    Escape(row.HsnOrSac ?? ""),
                    Escape(row.Category),
                    FormatDecimal(row.TaxableValue),
                    FormatDecimal(row.Cgst),
                    FormatDecimal(row.Sgst),
                    FormatDecimal(row.TotalValue)));
            }

            // TOTAL row per GSTIN group
            sb.AppendLine(string.Join(",",
                Escape(group.Gstin),
                "TOTAL",
                "",
                "",
                "",
                "",
                "",
                FormatDecimal(group.TotalTaxable),
                FormatDecimal(group.TotalCgst),
                FormatDecimal(group.TotalSgst),
                FormatDecimal(group.GrandTotal)));
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
