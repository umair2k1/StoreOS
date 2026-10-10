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
        var branding = new ShopBranding(string.IsNullOrWhiteSpace(shopName) ? "StoreOS" : shopName);
        return RenderDailySales(dto, branding);
    }

    public byte[] RenderDailySales(DailySalesReportDto dto, ShopBranding? branding)
    {
        var shopName = !string.IsNullOrWhiteSpace(branding?.ShopName) ? branding.ShopName : "StoreOS";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(8).FontColor("#0f172a"));

                // ── Header ────────────────────────────────────────────────────────
                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        // Left: Shop Logo & Details
                        row.RelativeItem().Row(brandRow =>
                        {
                            if (branding?.LogoBytes is not null && branding.LogoBytes.Length > 0)
                            {
                                brandRow.AutoItem().PaddingRight(10).MaxWidth(75).MaxHeight(50).Image(branding.LogoBytes).FitArea();
                            }

                            brandRow.RelativeItem().Column(col =>
                            {
                                col.Item().Text(shopName).FontSize(16).Bold().FontColor("#0f172a").LetterSpacing(0.04f);

                                if (!string.IsNullOrWhiteSpace(branding?.Address))
                                {
                                    col.Item().Text(branding.Address).FontSize(8).FontColor("#475569");
                                }

                                var contacts = new List<string>();
                                if (!string.IsNullOrWhiteSpace(branding?.Phone)) contacts.Add($"Phone: {branding.Phone}");
                                if (!string.IsNullOrWhiteSpace(branding?.Email)) contacts.Add($"Email: {branding.Email}");
                                if (contacts.Count > 0)
                                {
                                    col.Item().Text(string.Join("  |  ", contacts)).FontSize(8).FontColor("#475569");
                                }

                                if (!string.IsNullOrWhiteSpace(branding?.Gstin))
                                {
                                    col.Item().Text($"GSTIN: {branding.Gstin}").FontSize(8.5f).Bold().FontColor("#0f172a");
                                }
                            });
                        });

                        // Right: Report Title & Period
                        row.AutoItem().AlignRight().Column(metaCol =>
                        {
                            metaCol.Item().AlignRight().Text("DAILY SALES AUDIT REPORT").FontSize(13).Bold().FontColor("#0f172a");
                            metaCol.Item().AlignRight().Text($"Date: {dto.Date:dd-MMM-yyyy}").FontSize(10).Bold().FontColor("#2563eb");
                            metaCol.Item().AlignRight().Text($"Generated: {DateTime.UtcNow:dd-MMM-yyyy HH:mm} UTC").FontSize(7.5f).FontColor("#64748b");
                            metaCol.Item().PaddingTop(2).AlignRight().Text($"Total Invoices: {dto.Sales.InvoiceCount}  ·  Items Sold: {dto.Sales.ItemsSold}").FontSize(8).Bold().FontColor("#475569");
                        });
                    });

                    header.Item().PaddingTop(6).PaddingBottom(8).LineHorizontal(1.5f).LineColor("#0f172a");
                });

                // ── Content ───────────────────────────────────────────────────────
                page.Content().Column(col =>
                {
                    // 1. Executive Summary: 3-Box Grid
                    col.Item().Row(gridRow =>
                    {
                        // Box 1: Inventory Valuation Snapshot
                        gridRow.RelativeItem().Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(6).Column(box =>
                        {
                            box.Item().Text("INVENTORY SNAPSHOT").FontSize(7.5f).Bold().FontColor("#0f172a");
                            box.Item().LineHorizontal(0.5f).LineColor("#e2e8f0");
                            box.Item().PaddingTop(3);

                            box.Item().Row(r => { r.RelativeItem().Text("Stock Volume:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"{dto.Inventory.TotalStockVolume} units").FontSize(8).Bold(); });
                            box.Item().Row(r => { r.RelativeItem().Text("Worth @ Cost:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"₹{dto.Inventory.StockWorthAtCost:N2}").FontSize(8).Bold(); });
                            box.Item().Row(r => { r.RelativeItem().Text("Retail Valuation:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"₹{dto.Inventory.RetailValuation:N2}").FontSize(8).Bold(); });
                            box.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Low Stock:").FontSize(7.5f).FontColor("#475569");
                                var color = dto.Inventory.LowStockCount > 0 ? "#b45309" : "#0f172a";
                                r.AutoItem().Text($"{dto.Inventory.LowStockCount}").FontSize(8).Bold().FontColor(color);
                            });
                            box.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Out of Stock:").FontSize(7.5f).FontColor("#475569");
                                var color = dto.Inventory.OutOfStockCount > 0 ? "#dc2626" : "#0f172a";
                                r.AutoItem().Text($"{dto.Inventory.OutOfStockCount}").FontSize(8).Bold().FontColor(color);
                            });
                        });

                        gridRow.ConstantItem(8); // Spacer

                        // Box 2: Payment Breakdown
                        gridRow.RelativeItem().Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(6).Column(box =>
                        {
                            box.Item().Text("PAYMENT BREAKDOWN").FontSize(7.5f).Bold().FontColor("#0f172a");
                            box.Item().LineHorizontal(0.5f).LineColor("#e2e8f0");
                            box.Item().PaddingTop(3);

                            box.Item().Row(r => { r.RelativeItem().Text("Cash:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"₹{dto.Payments.Cash:N2}").FontSize(8).Bold(); });
                            box.Item().Row(r => { r.RelativeItem().Text("UPI / Digital:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"₹{dto.Payments.Upi:N2}").FontSize(8).Bold(); });
                            box.Item().Row(r => { r.RelativeItem().Text("Finance:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"₹{dto.Payments.Finance:N2}").FontSize(8).Bold(); });
                            box.Item().Row(r => { r.RelativeItem().Text("Other:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"₹{dto.Payments.Other:N2}").FontSize(8).Bold(); });
                            box.Item().Row(r => { r.RelativeItem().Text("TOTAL:").FontSize(8).Bold().FontColor("#0f172a"); r.AutoItem().Text($"₹{dto.Payments.Total:N2}").FontSize(8.5f).Bold().FontColor("#0f172a"); });

                            if (dto.OutstandingAmount > 0)
                            {
                                box.Item().Row(r => { r.RelativeItem().Text("Outstanding Due:").FontSize(7.5f).Bold().FontColor("#dc2626"); r.AutoItem().Text($"₹{dto.OutstandingAmount:N2}").FontSize(8).Bold().FontColor("#dc2626"); });
                            }
                        });

                        gridRow.ConstantItem(8); // Spacer

                        // Box 3: Sales & Commercial Summary
                        gridRow.RelativeItem().Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(6).Column(box =>
                        {
                            box.Item().Text("SALES PERFORMANCE").FontSize(7.5f).Bold().FontColor("#0f172a");
                            box.Item().LineHorizontal(0.5f).LineColor("#e2e8f0");
                            box.Item().PaddingTop(3);

                            box.Item().Row(r => { r.RelativeItem().Text("Total Revenue:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"₹{dto.Sales.Revenue:N2}").FontSize(8.5f).Bold().FontColor("#2563eb"); });
                            box.Item().Row(r => { r.RelativeItem().Text("Buying Cost (COGS):").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"₹{dto.Sales.BuyingCost:N2}").FontSize(8).Bold(); });
                            box.Item().Row(r => { r.RelativeItem().Text("Gross Daily Profit:").FontSize(7.5f).Bold().FontColor("#047857"); r.AutoItem().Text($"₹{dto.Sales.Profit:N2}").FontSize(8.5f).Bold().FontColor("#047857"); });
                            box.Item().Row(r => { r.RelativeItem().Text("GST Collected:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"₹{dto.Sales.GstCollected:N2}").FontSize(8).Bold(); });
                            box.Item().Row(r => { r.RelativeItem().Text("Invoices / Items:").FontSize(7.5f).FontColor("#475569"); r.AutoItem().Text($"{dto.Sales.InvoiceCount} inv · {dto.Sales.ItemsSold} items").FontSize(8); });
                        });
                    });

                    // 2. Sales by Category Section
                    col.Item().PaddingTop(12).Text("SALES BY PRODUCT CATEGORY").FontSize(9).Bold().FontColor("#0f172a");
                    col.Item().PaddingTop(2).Table(catTable =>
                    {
                        catTable.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3); // Category
                            c.ConstantColumn(80); // Items Sold
                            c.ConstantColumn(120); // Revenue
                            c.ConstantColumn(80); // Share %
                        });

                        catTable.Header(h =>
                        {
                            static IContainer HeaderStyle(IContainer cell) =>
                                cell.Background("#f1f5f9").BorderBottom(1.5f).BorderColor("#64748b").PaddingVertical(3).PaddingHorizontal(4);

                            h.Cell().Element(HeaderStyle).Text("Category").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignCenter().Text("Qty Sold").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignRight().Text("Revenue (₹)").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignRight().Text("Share (%)").FontSize(7.5f).Bold().FontColor("#334155");
                        });

                        foreach (var cat in dto.ByCategory)
                        {
                            var share = dto.Sales.Revenue > 0 ? (cat.Revenue / dto.Sales.Revenue * 100).ToString("F1") + "%" : "0.0%";

                            static IContainer RowStyle(IContainer cell) =>
                                cell.BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(3).PaddingHorizontal(4);

                            catTable.Cell().Element(RowStyle).Text(cat.Category).FontSize(8).Bold().FontColor("#0f172a");
                            catTable.Cell().Element(RowStyle).AlignCenter().Text(cat.ItemsSold.ToString()).FontSize(8);
                            catTable.Cell().Element(RowStyle).AlignRight().Text($"₹{cat.Revenue:N2}").FontSize(8).Bold();
                            catTable.Cell().Element(RowStyle).AlignRight().Text(share).FontSize(8).FontColor("#475569");
                        }

                        // Total Row
                        static IContainer TotalStyle(IContainer cell) =>
                            cell.Background("#f1f5f9").BorderTop(1f).BorderColor("#64748b").PaddingVertical(4).PaddingHorizontal(4);

                        catTable.Cell().Element(TotalStyle).Text("Total").FontSize(8).Bold().FontColor("#0f172a");
                        catTable.Cell().Element(TotalStyle).AlignCenter().Text(dto.Sales.ItemsSold.ToString()).FontSize(8).Bold();
                        catTable.Cell().Element(TotalStyle).AlignRight().Text($"₹{dto.Sales.Revenue:N2}").FontSize(8.5f).Bold().FontColor("#0f172a");
                        catTable.Cell().Element(TotalStyle).AlignRight().Text("100.0%").FontSize(8).Bold().FontColor("#0f172a");
                    });

                    // 3. Itemized Sales Log Section
                    col.Item().PaddingTop(14).Row(logHeader =>
                    {
                        logHeader.RelativeItem().Text($"ITEMIZED TRANSACTION LOG ({dto.ItemizedLog.Count} records)").FontSize(9).Bold().FontColor("#0f172a");
                        logHeader.AutoItem().Text("All Phones, Accessories & Services").FontSize(7.5f).FontColor("#64748b");
                    });

                    col.Item().PaddingTop(2);

                    if (dto.ItemizedLog.Count == 0)
                    {
                        col.Item().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(12).AlignCenter()
                            .Text("No sales transactions recorded on this date.").FontSize(8).Italic().FontColor("#64748b");
                    }
                    else
                    {
                        col.Item().Table(logTable =>
                        {
                            logTable.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(18);     // #
                                c.ConstantColumn(52);     // Date/Time
                                c.ConstantColumn(68);     // Invoice #
                                c.RelativeColumn(2.5f);   // Customer
                                c.RelativeColumn(3.5f);   // Item Description
                                c.ConstantColumn(24);     // Qty
                                c.ConstantColumn(52);     // Cost
                                c.ConstantColumn(55);     // Selling
                                c.ConstantColumn(52);     // Profit
                                c.ConstantColumn(42);     // Mode
                            });

                            logTable.Header(h =>
                            {
                                static IContainer HeaderStyle(IContainer cell) =>
                                    cell.Background("#f1f5f9").BorderBottom(1.5f).BorderColor("#64748b").PaddingVertical(3).PaddingHorizontal(3);

                                h.Cell().Element(HeaderStyle).AlignCenter().Text("#").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignCenter().Text("Date").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).Text("Invoice #").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).Text("Customer").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).Text("Item / IMEI").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignCenter().Text("Qty").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignRight().Text("Cost (₹)").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignRight().Text("Sell (₹)").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignRight().Text("Profit (₹)").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignCenter().Text("Mode").FontSize(7).Bold().FontColor("#334155");
                            });

                            for (int i = 0; i < dto.ItemizedLog.Count; i++)
                            {
                                var item = dto.ItemizedLog[i];
                                var profit = item.SellingValue - item.CostValue;
                                var profitColor = profit < 0 ? "#dc2626" : "#047857";

                                static IContainer RowStyle(IContainer cell) =>
                                    cell.BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(3).PaddingHorizontal(3);

                                logTable.Cell().Element(RowStyle).AlignCenter().Text((i + 1).ToString()).FontSize(7).FontColor("#64748b");
                                logTable.Cell().Element(RowStyle).AlignCenter().Text($"{item.Date:dd/MM}").FontSize(7).FontColor("#475569");
                                logTable.Cell().Element(RowStyle).Text(item.InvoiceNumber).FontSize(7.5f).Bold().FontColor("#0f172a");
                                logTable.Cell().Element(RowStyle).Text(item.CustomerName).FontSize(7.5f).FontColor("#334155");

                                logTable.Cell().Element(RowStyle).Column(ic =>
                                {
                                    ic.Item().Text(item.ItemName).FontSize(7.5f).Bold().FontColor("#0f172a");
                                    if (!string.IsNullOrWhiteSpace(item.Imei))
                                    {
                                        ic.Item().Text($"IMEI: {item.Imei}").FontSize(6.5f).Italic().FontColor("#64748b");
                                    }
                                });

                                logTable.Cell().Element(RowStyle).AlignCenter().Text(item.Qty.ToString()).FontSize(7.5f).Bold();
                                logTable.Cell().Element(RowStyle).AlignRight().Text(item.CostValue.ToString("F2")).FontSize(7.5f).FontColor("#64748b");
                                logTable.Cell().Element(RowStyle).AlignRight().Text(item.SellingValue.ToString("F2")).FontSize(7.5f).Bold();
                                logTable.Cell().Element(RowStyle).AlignRight().Text((profit >= 0 ? "+" : "") + profit.ToString("F2")).FontSize(7.5f).Bold().FontColor(profitColor);
                                logTable.Cell().Element(RowStyle).AlignCenter().Text(item.PaymentMode ?? "Cash").FontSize(7).FontColor("#475569");
                            }
                        });
                    }
                });

                // ── Footer ────────────────────────────────────────────────────────
                page.Footer().AlignCenter().Row(fRow =>
                {
                    fRow.RelativeItem().AlignLeft().Text("StoreOS Daily Sales Reconciliation Audit").FontSize(7).FontColor("#94a3b8");
                    fRow.RelativeItem().AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(7).FontColor("#94a3b8");
                        x.CurrentPageNumber().FontSize(7).FontColor("#94a3b8");
                        x.Span(" of ").FontSize(7).FontColor("#94a3b8");
                        x.TotalPages().FontSize(7).FontColor("#94a3b8");
                    });
                });
            });
        });

        return document.GeneratePdf();
    }

    public byte[] RenderInventoryExport(InventoryExportDto dto, string? shopName = null)
    {
        var branding = new ShopBranding(string.IsNullOrWhiteSpace(shopName) ? "StoreOS" : shopName);
        return RenderInventoryExport(dto, branding);
    }

    public byte[] RenderInventoryExport(InventoryExportDto dto, ShopBranding? branding)
    {
        var shopName = !string.IsNullOrWhiteSpace(branding?.ShopName) ? branding.ShopName : "StoreOS";

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(8).FontColor("#0f172a"));

                // ── Header ────────────────────────────────────────────────────────
                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        row.RelativeItem().Row(brandRow =>
                        {
                            if (branding?.LogoBytes is not null && branding.LogoBytes.Length > 0)
                            {
                                brandRow.AutoItem().PaddingRight(10).MaxWidth(75).MaxHeight(50).Image(branding.LogoBytes).FitArea();
                            }

                            brandRow.RelativeItem().Column(col =>
                            {
                                col.Item().Text(shopName).FontSize(16).Bold().FontColor("#0f172a").LetterSpacing(0.04f);
                                if (!string.IsNullOrWhiteSpace(branding?.Address)) col.Item().Text(branding.Address).FontSize(8).FontColor("#475569");
                                if (!string.IsNullOrWhiteSpace(branding?.Gstin)) col.Item().Text($"GSTIN: {branding.Gstin}").FontSize(8.5f).Bold().FontColor("#0f172a");
                            });
                        });

                        row.AutoItem().AlignRight().Column(metaCol =>
                        {
                            metaCol.Item().AlignRight().Text("INVENTORY & SERVICE CATALOG EXPORT").FontSize(12).Bold().FontColor("#0f172a");
                            metaCol.Item().AlignRight().Text($"Generated: {DateTime.UtcNow:dd-MMM-yyyy HH:mm} UTC").FontSize(7.5f).FontColor("#64748b");
                            metaCol.Item().PaddingTop(2).AlignRight().Text($"Physical Items: {dto.PhysicalItems.Count}  ·  Services: {dto.Services.Count}").FontSize(8).Bold().FontColor("#475569");
                        });
                    });

                    header.Item().PaddingTop(6).PaddingBottom(8).LineHorizontal(1.5f).LineColor("#0f172a");
                });

                // ── Content ───────────────────────────────────────────────────────
                page.Content().Column(col =>
                {
                    // Section 1: Physical Inventory Items
                    col.Item().Text($"PHYSICAL INVENTORY ITEMS ({dto.PhysicalItems.Count} SKUs)").FontSize(9.5f).Bold().FontColor("#0f172a");
                    col.Item().PaddingTop(3);

                    if (dto.PhysicalItems.Count == 0)
                    {
                        col.Item().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(10).AlignCenter()
                            .Text("No physical inventory items found.").FontSize(8).Italic().FontColor("#64748b");
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(18);     // #
                                c.RelativeColumn(3.5f);   // Item Description
                                c.ConstantColumn(65);     // Category
                                c.ConstantColumn(30);     // Qty
                                c.ConstantColumn(58);     // Cost (₹)
                                c.ConstantColumn(58);     // Selling (₹)
                                c.ConstantColumn(35);     // GST%
                                c.ConstantColumn(65);     // Valuation (₹)
                            });

                            table.Header(h =>
                            {
                                static IContainer HeaderStyle(IContainer cell) =>
                                    cell.Background("#f1f5f9").BorderBottom(1.5f).BorderColor("#64748b").PaddingVertical(3).PaddingHorizontal(3);

                                h.Cell().Element(HeaderStyle).AlignCenter().Text("#").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).Text("Item / IMEI Details").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignCenter().Text("Category").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignCenter().Text("Qty").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignRight().Text("Cost (₹)").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignRight().Text("Sell Price (₹)").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignCenter().Text("GST").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignRight().Text("Valuation (₹)").FontSize(7).Bold().FontColor("#334155");
                            });

                            for (int i = 0; i < dto.PhysicalItems.Count; i++)
                            {
                                var p = dto.PhysicalItems[i];
                                var valuation = p.Quantity * p.SellingPrice;

                                static IContainer RowStyle(IContainer cell) =>
                                    cell.BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(3).PaddingHorizontal(3);

                                table.Cell().Element(RowStyle).AlignCenter().Text((i + 1).ToString()).FontSize(7).FontColor("#64748b");
                                table.Cell().Element(RowStyle).Column(ic =>
                                {
                                    ic.Item().Text(p.Item).FontSize(8).Bold().FontColor("#0f172a");
                                    if (!string.IsNullOrWhiteSpace(p.ImeiSummary))
                                    {
                                        ic.Item().Text($"IMEIs: {p.ImeiSummary}").FontSize(6.5f).Italic().FontColor("#64748b");
                                    }
                                });

                                table.Cell().Element(RowStyle).AlignCenter().Text(p.Category).FontSize(7.5f).FontColor("#475569");
                                table.Cell().Element(RowStyle).AlignCenter().Text(p.Quantity.ToString()).FontSize(8).Bold();
                                table.Cell().Element(RowStyle).AlignRight().Text(p.PurchasePrice.ToString("F2")).FontSize(7.5f).FontColor("#64748b");
                                table.Cell().Element(RowStyle).AlignRight().Text(p.SellingPrice.ToString("F2")).FontSize(8).Bold();
                                table.Cell().Element(RowStyle).AlignCenter().Text($"{p.Gst:F0}%").FontSize(7.5f).FontColor("#475569");
                                table.Cell().Element(RowStyle).AlignRight().Text(valuation.ToString("F2")).FontSize(8).Bold().FontColor("#0f172a");
                            }
                        });
                    }

                    // Section 2: Services Catalog
                    col.Item().PaddingTop(14).Text($"REPAIR & SERVICE CATALOG ({dto.Services.Count} Services)").FontSize(9.5f).Bold().FontColor("#0f172a");
                    col.Item().PaddingTop(3);

                    if (dto.Services.Count == 0)
                    {
                        col.Item().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(10).AlignCenter()
                            .Text("No repair services defined.").FontSize(8).Italic().FontColor("#64748b");
                    }
                    else
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(18);     // #
                                c.RelativeColumn(3);      // Service
                                c.RelativeColumn(4);      // Description
                                c.ConstantColumn(65);     // Charge (₹)
                                c.ConstantColumn(40);     // GST%
                                c.ConstantColumn(50);     // SAC
                            });

                            table.Header(h =>
                            {
                                static IContainer HeaderStyle(IContainer cell) =>
                                    cell.Background("#f1f5f9").BorderBottom(1.5f).BorderColor("#64748b").PaddingVertical(3).PaddingHorizontal(3);

                                h.Cell().Element(HeaderStyle).AlignCenter().Text("#").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).Text("Service Name").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).Text("Description").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignRight().Text("Charge (₹)").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignCenter().Text("GST").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HeaderStyle).AlignCenter().Text("SAC Code").FontSize(7).Bold().FontColor("#334155");
                            });

                            for (int i = 0; i < dto.Services.Count; i++)
                            {
                                var s = dto.Services[i];

                                static IContainer RowStyle(IContainer cell) =>
                                    cell.BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(3).PaddingHorizontal(3);

                                table.Cell().Element(RowStyle).AlignCenter().Text((i + 1).ToString()).FontSize(7).FontColor("#64748b");
                                table.Cell().Element(RowStyle).Text(s.Service).FontSize(8).Bold().FontColor("#0f172a");
                                table.Cell().Element(RowStyle).Text(s.Description ?? "—").FontSize(7.5f).FontColor("#475569");
                                table.Cell().Element(RowStyle).AlignRight().Text(s.ServiceCharge.ToString("F2")).FontSize(8).Bold();
                                table.Cell().Element(RowStyle).AlignCenter().Text($"{s.Gst:F0}%").FontSize(7.5f).FontColor("#475569");
                                table.Cell().Element(RowStyle).AlignCenter().Text(s.Sac ?? "—").FontSize(7.5f).FontColor("#64748b");
                            }
                        });
                    }
                });

                // ── Footer ────────────────────────────────────────────────────────
                page.Footer().AlignCenter().Row(fRow =>
                {
                    fRow.RelativeItem().AlignLeft().Text("StoreOS Master Inventory Catalog Export").FontSize(7).FontColor("#94a3b8");
                    fRow.RelativeItem().AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(7).FontColor("#94a3b8");
                        x.CurrentPageNumber().FontSize(7).FontColor("#94a3b8");
                        x.Span(" of ").FontSize(7).FontColor("#94a3b8");
                        x.TotalPages().FontSize(7).FontColor("#94a3b8");
                    });
                });
            });
        });

        return document.GeneratePdf();
    }
}
