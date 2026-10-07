using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace StoreOS.Infrastructure.Printing;

public class InvoicePdfRenderer
{
    public byte[] Render(InvoicePdfDto dto)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.Header().Column(col =>
                {
                    col.Item().Text(dto.ShopName).FontSize(18).Bold();
                    if (dto.ShopAddress is not null) col.Item().Text(dto.ShopAddress);
                    if (dto.ShopPhone is not null) col.Item().Text($"Phone: {dto.ShopPhone}");
                    if (dto.ShopGstin is not null) col.Item().Text($"GSTIN: {dto.ShopGstin}");
                    col.Item().PaddingTop(10).Text($"{dto.InvoiceType} #{dto.InvoiceNumber}").FontSize(14).Bold();
                    col.Item().Text($"Date: {dto.SaleDate:dd-MMM-yyyy HH:mm}");
                    col.Item().Text($"Customer: {dto.CustomerName}" + (dto.CustomerGstin is not null ? $" (GSTIN: {dto.CustomerGstin})" : ""));
                });
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(3);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Text("Item").Bold();
                        h.Cell().Text("Qty").Bold();
                        h.Cell().Text("Price").Bold();
                        h.Cell().Text("Taxable").Bold();
                        h.Cell().Text("CGST").Bold();
                        h.Cell().Text("SGST").Bold();
                    });
                    foreach (var line in dto.Lines)
                    {
                        var label = line.ItemName + (line.IsGift ? " (Gift)" : "") + (line.Imei is not null ? $" [IMEI: {line.Imei}]" : "");
                        table.Cell().Text(label);
                        table.Cell().Text(line.Qty.ToString());
                        table.Cell().Text(line.PricePerUnit.ToString("F2"));
                        table.Cell().Text(line.Taxable.ToString("F2"));
                        table.Cell().Text(line.Cgst.ToString("F2"));
                        table.Cell().Text(line.Sgst.ToString("F2"));
                    }
                });
                page.Footer().Column(col =>
                {
                    col.Item().Text($"Taxable: {dto.TotalTaxable:F2}  CGST: {dto.TotalCgst:F2}  SGST: {dto.TotalSgst:F2}");
                    col.Item().Text($"Discount: {dto.Discount:F2}  Grand Total: {dto.GrandTotal:F2}").Bold();
                    col.Item().Text($"Paid: {dto.AmountPaid:F2}  Balance: {dto.Balance:F2}");
                    if (dto.FinanceProvider is not null) col.Item().Text($"Financed via: {dto.FinanceProvider} ({dto.FinancerName})");
                    if (dto.IrnDisplay is not null) col.Item().Text($"IRN: {dto.IrnDisplay}"); // reserved slot, Phase 8 roadmap
                });
            });
        });
        return document.GeneratePdf();
    }
}
