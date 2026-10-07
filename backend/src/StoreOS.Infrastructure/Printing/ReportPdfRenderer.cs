using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using StoreOS.Application.Reporting.DailySales;
using StoreOS.Application.Reporting.InventoryExport;

namespace StoreOS.Infrastructure.Printing;

public class ReportPdfRenderer
{
    public byte[] RenderDailySales(DailySalesReportDto dto, string? shopName = null)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);

                page.Header().Column(col =>
                {
                    if (!string.IsNullOrWhiteSpace(shopName))
                    {
                        col.Item().Text(shopName).FontSize(18).Bold();
                    }
                    col.Item().Text($"Daily Sales Audit Report — {dto.Date:dd-MMM-yyyy}").FontSize(14).Bold();
                    col.Item().Text($"Generated: {DateTime.UtcNow:dd-MMM-yyyy HH:mm} UTC").FontSize(9).FontColor(Colors.Grey.Medium);
                });

                page.Content().Column(col =>
                {
                    // 1. Sales & Collections Summary
                    col.Item().PaddingTop(10).Text("Sales & Commercial Summary").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });

                        table.Cell().Text($"Revenue: {dto.Sales.Revenue:F2}").Bold();
                        table.Cell().Text($"COGS: {dto.Sales.BuyingCost:F2}");
                        table.Cell().Text($"Gross Profit: {dto.Sales.Profit:F2}").Bold();
                        table.Cell().Text($"GST Collected: {dto.Sales.GstCollected:F2}");

                        table.Cell().Text($"Invoices: {dto.Sales.InvoiceCount}");
                        table.Cell().Text($"Items Sold: {dto.Sales.ItemsSold}");
                        table.Cell().Text($"Collected: {dto.CollectedAmount:F2}").Bold();
                        table.Cell().Text($"Outstanding: {dto.OutstandingAmount:F2}");
                    });

                    // 2. Inventory Valuation Snapshot
                    col.Item().PaddingTop(10).Text("Inventory Valuation Snapshot").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });

                        table.Cell().Text($"Stock Volume: {dto.Inventory.TotalStockVolume}");
                        table.Cell().Text($"Worth @ Cost: {dto.Inventory.StockWorthAtCost:F2}");
                        table.Cell().Text($"Retail Valuation: {dto.Inventory.RetailValuation:F2}");
                        table.Cell().Text($"Low Stock: {dto.Inventory.LowStockCount}");
                        table.Cell().Text($"Out of Stock: {dto.Inventory.OutOfStockCount}");
                    });

                    // 3. Payment Breakdown Table
                    col.Item().PaddingTop(10).Text("Payment Breakdown").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Cash").Bold();
                            h.Cell().Text("UPI").Bold();
                            h.Cell().Text("Finance").Bold();
                            h.Cell().Text("Other").Bold();
                            h.Cell().Text("Total").Bold();
                        });
                        table.Cell().Text(dto.Payments.Cash.ToString("F2"));
                        table.Cell().Text(dto.Payments.Upi.ToString("F2"));
                        table.Cell().Text(dto.Payments.Finance.ToString("F2"));
                        table.Cell().Text(dto.Payments.Other.ToString("F2"));
                        table.Cell().Text(dto.Payments.Total.ToString("F2")).Bold();
                    });

                    // 4. Category Breakdown Table
                    col.Item().PaddingTop(10).Text("Sales by Category").FontSize(12).Bold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Category").Bold();
                            h.Cell().Text("Revenue").Bold();
                            h.Cell().Text("Items Sold").Bold();
                        });
                        foreach (var cat in dto.ByCategory)
                        {
                            table.Cell().Text(cat.Category);
                            table.Cell().Text(cat.Revenue.ToString("F2"));
                            table.Cell().Text(cat.ItemsSold.ToString());
                        }
                    });

                    // 5. Itemized Sales Log Table
                    col.Item().PaddingTop(10).Text("Itemized Sales Log").FontSize(12).Bold();
                    if (dto.ItemizedLog.Count == 0)
                    {
                        col.Item().Text("No sales recorded for this date.").Italic().FontColor(Colors.Grey.Medium);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                                c.RelativeColumn(3);
                                c.RelativeColumn(1);
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Text("Invoice").Bold();
                                h.Cell().Text("Customer").Bold();
                                h.Cell().Text("Item").Bold();
                                h.Cell().Text("Qty").Bold();
                                h.Cell().Text("Selling").Bold();
                                h.Cell().Text("Mode").Bold();
                            });
                            foreach (var item in dto.ItemizedLog)
                            {
                                table.Cell().Text(item.InvoiceNumber);
                                table.Cell().Text(item.CustomerName);
                                var itemLabel = item.ItemName + (item.Imei is not null ? $" [IMEI: {item.Imei}]" : "");
                                table.Cell().Text(itemLabel);
                                table.Cell().Text(item.Qty.ToString());
                                table.Cell().Text(item.SellingValue.ToString("F2"));
                                table.Cell().Text(item.PaymentMode ?? "-");
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }

    public byte[] RenderInventoryExport(InventoryExportDto dto, string? shopName = null)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);

                page.Header().Column(col =>
                {
                    if (!string.IsNullOrWhiteSpace(shopName))
                    {
                        col.Item().Text(shopName).FontSize(18).Bold();
                    }
                    col.Item().Text("Inventory & Service Catalog Export").FontSize(14).Bold();
                    col.Item().Text($"Generated: {DateTime.UtcNow:dd-MMM-yyyy HH:mm} UTC").FontSize(9).FontColor(Colors.Grey.Medium);
                });

                page.Content().Column(col =>
                {
                    // Section 1: Physical Items
                    col.Item().PaddingTop(10).Text("Physical Inventory Items").FontSize(12).Bold();
                    if (dto.PhysicalItems.Count == 0)
                    {
                        col.Item().Text("No physical inventory items found.").Italic().FontColor(Colors.Grey.Medium);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3);
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                                c.RelativeColumn(2);
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Text("Item").Bold();
                                h.Cell().Text("Category").Bold();
                                h.Cell().Text("Qty").Bold();
                                h.Cell().Text("Cost").Bold();
                                h.Cell().Text("Sell Price").Bold();
                                h.Cell().Text("GST%").Bold();
                            });
                            foreach (var p in dto.PhysicalItems)
                            {
                                var itemLabel = p.Item + (p.ImeiSummary is not null ? $" ({p.ImeiSummary})" : "");
                                table.Cell().Text(itemLabel);
                                table.Cell().Text(p.Category);
                                table.Cell().Text(p.Quantity.ToString());
                                table.Cell().Text(p.PurchasePrice.ToString("F2"));
                                table.Cell().Text(p.SellingPrice.ToString("F2"));
                                table.Cell().Text(p.Gst.ToString("F0") + "%");
                            }
                        });
                    }

                    // Section 2: Services
                    col.Item().PaddingTop(15).Text("Repair & Service Catalog").FontSize(12).Bold();
                    if (dto.Services.Count == 0)
                    {
                        col.Item().Text("No services defined.").Italic().FontColor(Colors.Grey.Medium);
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3);
                                c.RelativeColumn(3);
                                c.RelativeColumn(2);
                                c.RelativeColumn(1);
                                c.RelativeColumn(1);
                            });
                            table.Header(h =>
                            {
                                h.Cell().Text("Service").Bold();
                                h.Cell().Text("Description").Bold();
                                h.Cell().Text("Charge").Bold();
                                h.Cell().Text("GST%").Bold();
                                h.Cell().Text("SAC").Bold();
                            });
                            foreach (var s in dto.Services)
                            {
                                table.Cell().Text(s.Service);
                                table.Cell().Text(s.Description ?? "-");
                                table.Cell().Text(s.ServiceCharge.ToString("F2"));
                                table.Cell().Text(s.Gst.ToString("F0") + "%");
                                table.Cell().Text(s.Sac ?? "-");
                            }
                        });
                    }
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" of ");
                    x.TotalPages();
                });
            });
        });

        return document.GeneratePdf();
    }
}
