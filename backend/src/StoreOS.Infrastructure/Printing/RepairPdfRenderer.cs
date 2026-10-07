using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace StoreOS.Infrastructure.Printing;

public class RepairPdfRenderer
{
    public byte[] Render(RepairPdfDto dto)
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
                    col.Item().PaddingTop(10).Text($"Repair Job #{dto.JobNumber}").FontSize(14).Bold();
                    col.Item().Text($"Status: {dto.Status}");
                    col.Item().Text($"Received: {dto.ReceivedAt:dd-MMM-yyyy HH:mm}");
                    col.Item().Text($"Customer: {dto.CustomerName}" + (dto.CustomerPhone is not null ? $" ({dto.CustomerPhone})" : ""));
                });

                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(10).PaddingBottom(5).Text("Device & Issue Details").FontSize(12).Bold();
                    if (dto.DeviceBrand is not null || dto.DeviceModel is not null)
                    {
                        col.Item().Text($"Device: {dto.DeviceBrand} {dto.DeviceModel}".Trim());
                    }
                    if (dto.Imei1 is not null) col.Item().Text($"IMEI: {dto.Imei1}");
                    if (dto.SerialNumber is not null) col.Item().Text($"Serial No: {dto.SerialNumber}");
                    if (dto.ProblemDescription is not null)
                    {
                        col.Item().PaddingTop(5).Text($"Reported Problem: {dto.ProblemDescription}");
                    }

                    if (dto.Parts is not null && dto.Parts.Count > 0)
                    {
                        col.Item().PaddingTop(15).PaddingBottom(5).Text("Parts & Services").FontSize(12).Bold();
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Text("Part / Service").Bold();
                                h.Cell().Text("Qty").Bold();
                                h.Cell().Text("Charge").Bold();
                            });
                            foreach (var part in dto.Parts)
                            {
                                table.Cell().Text(part.PartName);
                                table.Cell().Text(part.Quantity.ToString());
                                table.Cell().Text(part.ChargeAmount.ToString("F2"));
                            }
                        });
                    }
                });

                page.Footer().Column(col =>
                {
                    if (dto.EstimatedCost.HasValue) col.Item().Text($"Estimated Cost: {dto.EstimatedCost.Value:F2}");
                    if (dto.AdvancePaid.HasValue) col.Item().Text($"Advance Paid: {dto.AdvancePaid.Value:F2}");
                    if (dto.FinalTotal.HasValue) col.Item().Text($"Final Total: {dto.FinalTotal.Value:F2}").Bold();
                    if (dto.BalanceDue.HasValue) col.Item().Text($"Balance Due: {dto.BalanceDue.Value:F2}");
                    if (dto.WarrantyUntil.HasValue) col.Item().Text($"Warranty Until: {dto.WarrantyUntil.Value:dd-MMM-yyyy}");
                    if (dto.InvoiceNumber is not null) col.Item().Text($"Billed on Invoice: #{dto.InvoiceNumber}").Bold();
                });
            });
        });

        return document.GeneratePdf();
    }
}
