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
                page.Margin(24);
                page.DefaultTextStyle(x => x.FontSize(8.5f).FontColor("#0f172a"));

                // ── Header ────────────────────────────────────────────────────────
                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        // Left: Shop Logo & Details
                        row.RelativeItem().Row(brandRow =>
                        {
                            if (dto.LogoBytes is not null && dto.LogoBytes.Length > 0)
                            {
                                brandRow.AutoItem().PaddingRight(10).MaxWidth(75).MaxHeight(50).Image(dto.LogoBytes).FitArea();
                            }

                            brandRow.RelativeItem().Column(col =>
                            {
                                col.Item().Text(dto.ShopName).FontSize(16).Bold().FontColor("#0f172a").LetterSpacing(0.04f);
                                if (!string.IsNullOrWhiteSpace(dto.ShopAddress)) col.Item().Text(dto.ShopAddress).FontSize(8).FontColor("#475569");
                                if (!string.IsNullOrWhiteSpace(dto.ShopPhone)) col.Item().Text($"Phone: {dto.ShopPhone}").FontSize(8).FontColor("#475569");
                            });
                        });

                        // Right: Return Note Metadata Card
                        row.AutoItem().Width(180).Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(6).Column(metaCol =>
                        {
                            metaCol.Item().AlignCenter().Text("SUPPLIER RETURN NOTE").FontSize(10.5f).Bold().FontColor("#0f172a");
                            metaCol.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem().Text("Note #:").FontSize(8).FontColor("#64748b");
                                r.AutoItem().Text(dto.ReturnNumber).FontSize(8.5f).Bold().FontColor("#0f172a");
                            });
                            metaCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Date:").FontSize(8).FontColor("#64748b");
                                r.AutoItem().Text($"{dto.ReturnDate:dd/MM/yyyy}").FontSize(8).FontColor("#0f172a");
                            });
                            metaCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Settlement:").FontSize(8).FontColor("#64748b");
                                r.AutoItem().Text(dto.SettlementType).FontSize(8).Bold().FontColor("#2563eb");
                            });
                        });
                    });

                    header.Item().PaddingTop(6).PaddingBottom(6).LineHorizontal(1.5f).LineColor("#0f172a");

                    // Supplier Card
                    header.Item().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(6).Row(suppRow =>
                    {
                        suppRow.RelativeItem().Column(sc =>
                        {
                            sc.Item().Text("SUPPLIER / VENDOR:").FontSize(7.5f).Bold().FontColor("#64748b");
                            sc.Item().Text(dto.SupplierName).FontSize(9.5f).Bold().FontColor("#0f172a");
                            if (!string.IsNullOrWhiteSpace(dto.SupplierPhone)) sc.Item().Text($"Contact: {dto.SupplierPhone}").FontSize(8).FontColor("#475569");
                            if (!string.IsNullOrWhiteSpace(dto.SupplierAddress)) sc.Item().Text($"Address: {dto.SupplierAddress}").FontSize(8).FontColor("#475569");
                        });
                    });
                });

                // ── Content ───────────────────────────────────────────────────────
                page.Content().Column(col =>
                {
                    col.Item().PaddingTop(10).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(22);     // S.No
                            c.RelativeColumn(4);      // Description
                            c.ConstantColumn(40);     // Qty
                            c.ConstantColumn(70);     // Rate (₹)
                            c.ConstantColumn(80);     // Total (₹)
                        });

                        table.Header(h =>
                        {
                            static IContainer HStyle(IContainer cell) =>
                                cell.Background("#f1f5f9").BorderBottom(1.5f).BorderColor("#64748b").PaddingVertical(3).PaddingHorizontal(3);

                            h.Cell().Element(HStyle).AlignCenter().Text("#").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HStyle).Text("Item Description & Serial / IMEI").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HStyle).AlignCenter().Text("Qty").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HStyle).AlignRight().Text("Rate (₹)").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HStyle).AlignRight().Text("Line Total (₹)").FontSize(7.5f).Bold().FontColor("#334155");
                        });

                        for (int idx = 0; idx < dto.Lines.Count; idx++)
                        {
                            var line = dto.Lines[idx];
                            static IContainer RStyle(IContainer cell) =>
                                cell.BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(4).PaddingHorizontal(3);

                            table.Cell().Element(RStyle).AlignCenter().Text((idx + 1).ToString()).FontSize(7.5f).FontColor("#64748b");
                            table.Cell().Element(RStyle).Column(ic =>
                            {
                                ic.Item().Text(line.ItemName).FontSize(8.5f).Bold().FontColor("#0f172a");
                                if (line.ImeiNumbers is not null && line.ImeiNumbers.Length > 0)
                                {
                                    ic.Item().Text($"IMEIs: {string.Join(", ", line.ImeiNumbers)}").FontSize(7).Italic().FontColor("#64748b");
                                }
                            });

                            table.Cell().Element(RStyle).AlignCenter().Text(line.Qty.ToString()).FontSize(8).Bold();
                            table.Cell().Element(RStyle).AlignRight().Text(line.ReturnRate.ToString("F2")).FontSize(8);
                            table.Cell().Element(RStyle).AlignRight().Text(line.LineTotal.ToString("F2")).FontSize(8.5f).Bold().FontColor("#0f172a");
                        }
                    });

                    // Reason & Totals
                    col.Item().PaddingTop(10).Row(br =>
                    {
                        br.RelativeItem(6).Border(1).BorderColor("#e2e8f0").Background("#fafafa").Padding(6).Column(rc =>
                        {
                            rc.Item().Text("RETURN REASON & AUDIT NOTES").FontSize(7.5f).Bold().FontColor("#475569");
                            rc.Item().LineHorizontal(0.5f).LineColor("#e2e8f0");
                            rc.Item().PaddingTop(3);
                            rc.Item().Text(string.IsNullOrWhiteSpace(dto.Reason) ? "Vendor debit return." : dto.Reason).FontSize(8).FontColor("#0f172a");
                        });

                        br.ConstantItem(12);

                        br.RelativeItem(4).Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(6).Column(tc =>
                        {
                            tc.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Total Units:").FontSize(8).FontColor("#475569");
                                r.AutoItem().Text(dto.Lines.Sum(l => l.Qty).ToString()).FontSize(8).Bold();
                            });
                            tc.Item().PaddingTop(2).LineHorizontal(1f).LineColor("#0f172a");
                            tc.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem().Text("TOTAL DEBIT:").FontSize(10).Bold().FontColor("#0f172a");
                                r.AutoItem().Text($"₹{dto.TotalAmount:F2}").FontSize(10).Bold().FontColor("#0f172a");
                            });
                        });
                    });

                    // Signatures
                    col.Item().PaddingTop(16).Row(sRow =>
                    {
                        sRow.RelativeItem().Column(c =>
                        {
                            c.Item().Width(120).LineHorizontal(0.75f).LineColor("#94a3b8");
                            c.Item().PaddingTop(2).Text("Supplier Representative").FontSize(7.5f).Bold().FontColor("#64748b");
                        });

                        sRow.AutoItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Width(140).LineHorizontal(0.75f).LineColor("#94a3b8");
                            c.Item().PaddingTop(2).AlignRight().Text($"For {dto.ShopName}").FontSize(7.5f).Bold().FontColor("#64748b");
                        });
                    });
                });

                // ── Footer ────────────────────────────────────────────────────────
                page.Footer().AlignCenter().Row(fRow =>
                {
                    fRow.RelativeItem().AlignLeft().Text("StoreOS Supplier Returns & Accounts Payable").FontSize(7).FontColor("#94a3b8");
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
