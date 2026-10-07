using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace StoreOS.Infrastructure.Printing;

public class SupplierReturnPdfRenderer
{
    public byte[] Render(SupplierReturnPdfDto dto)
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
                    col.Item().PaddingTop(10).Text($"Supplier Return Note #{dto.ReturnNumber}").FontSize(14).Bold();
                    col.Item().Text($"Date: {dto.ReturnDate:dd-MMM-yyyy}");
                    col.Item().PaddingTop(5).Text($"Supplier: {dto.SupplierName}").Bold();
                    if (dto.SupplierPhone is not null) col.Item().Text($"Phone: {dto.SupplierPhone}");
                    if (dto.SupplierAddress is not null) col.Item().Text($"Address: {dto.SupplierAddress}");
                });

                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(15).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Item Description").Bold();
                            h.Cell().Text("Qty").Bold();
                            h.Cell().Text("Rate").Bold();
                            h.Cell().Text("Total").Bold();
                        });
                        foreach (var line in dto.Lines)
                        {
                            var itemLabel = line.ItemName;
                            if (line.ImeiNumbers is not null && line.ImeiNumbers.Length > 0)
                            {
                                itemLabel += $" [IMEIs: {string.Join(", ", line.ImeiNumbers)}]";
                            }
                            table.Cell().Text(itemLabel);
                            table.Cell().Text(line.Qty.ToString());
                            table.Cell().Text(line.ReturnRate.ToString("F2"));
                            table.Cell().Text(line.LineTotal.ToString("F2"));
                        }
                    });
                });

                page.Footer().Column(col =>
                {
                    col.Item().Text($"Total Amount: {dto.TotalAmount:F2}").FontSize(12).Bold();
                    col.Item().Text($"Settlement: {dto.SettlementType}");
                    if (dto.Reason is not null) col.Item().Text($"Reason: {dto.Reason}");
                });
            });
        });

        return document.GeneratePdf();
    }
}
