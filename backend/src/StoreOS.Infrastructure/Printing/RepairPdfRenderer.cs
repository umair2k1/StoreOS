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

                        // Right: Ticket Badge
                        row.AutoItem().Width(175).Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(6).Column(metaCol =>
                        {
                            metaCol.Item().AlignCenter().Text("REPAIR INTAKE TICKET").FontSize(10.5f).Bold().FontColor("#0f172a");
                            metaCol.Item().PaddingTop(2).Row(r =>
                            {
                                r.RelativeItem().Text("Ticket #:").FontSize(8).FontColor("#64748b");
                                r.AutoItem().Text(dto.JobNumber).FontSize(9).Bold().FontColor("#2563eb");
                            });
                            metaCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Received:").FontSize(8).FontColor("#64748b");
                                r.AutoItem().Text($"{dto.ReceivedAt:dd/MM/yyyy HH:mm}").FontSize(8).FontColor("#0f172a");
                            });
                            metaCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Status:").FontSize(8).FontColor("#64748b");
                                r.AutoItem().Text(dto.Status).FontSize(8).Bold().FontColor("#0f172a");
                            });
                        });
                    });

                    header.Item().PaddingTop(6).PaddingBottom(6).LineHorizontal(1.5f).LineColor("#0f172a");
                });

                // ── Content ───────────────────────────────────────────────────────
                page.Content().Column(col =>
                {
                    // 2-Column: Customer Info & Device Specs
                    col.Item().Row(r =>
                    {
                        // Customer info
                        r.RelativeItem().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(6).Column(c =>
                        {
                            c.Item().Text("CUSTOMER INFORMATION").FontSize(7.5f).Bold().FontColor("#64748b");
                            c.Item().LineHorizontal(0.5f).LineColor("#e2e8f0");
                            c.Item().PaddingTop(2);
                            c.Item().Text(dto.CustomerName).FontSize(9.5f).Bold().FontColor("#0f172a");
                            if (!string.IsNullOrWhiteSpace(dto.CustomerPhone))
                            {
                                c.Item().Text($"Contact: {dto.CustomerPhone}").FontSize(8).FontColor("#475569");
                            }
                        });

                        r.ConstantItem(10); // Spacer

                        // Device specs
                        r.RelativeItem().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(6).Column(d =>
                        {
                            d.Item().Text("DEVICE SPECIFICATION").FontSize(7.5f).Bold().FontColor("#64748b");
                            d.Item().LineHorizontal(0.5f).LineColor("#e2e8f0");
                            d.Item().PaddingTop(2);
                            var devName = $"{dto.DeviceBrand} {dto.DeviceModel}".Trim();
                            d.Item().Text(string.IsNullOrWhiteSpace(devName) ? "Mobile Device" : devName).FontSize(9.5f).Bold().FontColor("#0f172a");
                            if (!string.IsNullOrWhiteSpace(dto.Imei1)) d.Item().Text($"IMEI: {dto.Imei1}").FontSize(8).FontColor("#475569");
                            if (!string.IsNullOrWhiteSpace(dto.SerialNumber)) d.Item().Text($"Serial #: {dto.SerialNumber}").FontSize(8).FontColor("#475569");
                        });
                    });

                    // Problem description & diagnostics
                    col.Item().PaddingTop(8).Border(1).BorderColor("#e2e8f0").Background("#fafafa").Padding(6).Column(p =>
                    {
                        p.Item().Text("REPORTED ISSUE & DIAGNOSTIC NOTES").FontSize(7.5f).Bold().FontColor("#475569");
                        p.Item().LineHorizontal(0.5f).LineColor("#e2e8f0");
                        p.Item().PaddingTop(3);
                        p.Item().Text(string.IsNullOrWhiteSpace(dto.ProblemDescription) ? "Standard diagnosis requested." : dto.ProblemDescription)
                            .FontSize(8.5f).FontColor("#0f172a");
                    });

                    // Parts and Services table if present
                    if (dto.Parts is not null && dto.Parts.Count > 0)
                    {
                        col.Item().PaddingTop(10).Text($"PARTS & SERVICES ALLOCATED ({dto.Parts.Count})").FontSize(8.5f).Bold().FontColor("#0f172a");
                        col.Item().PaddingTop(2).Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(20);
                                c.RelativeColumn(4);
                                c.ConstantColumn(40);
                                c.ConstantColumn(70);
                            });

                            table.Header(h =>
                            {
                                static IContainer HStyle(IContainer cell) =>
                                    cell.Background("#f1f5f9").BorderBottom(1f).BorderColor("#64748b").PaddingVertical(3).PaddingHorizontal(3);

                                h.Cell().Element(HStyle).AlignCenter().Text("#").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HStyle).Text("Part / Service Item").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HStyle).AlignCenter().Text("Qty").FontSize(7).Bold().FontColor("#334155");
                                h.Cell().Element(HStyle).AlignRight().Text("Charge (₹)").FontSize(7).Bold().FontColor("#334155");
                            });

                            for (int idx = 0; idx < dto.Parts.Count; idx++)
                            {
                                var part = dto.Parts[idx];
                                static IContainer RStyle(IContainer cell) =>
                                    cell.BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(3).PaddingHorizontal(3);

                                table.Cell().Element(RStyle).AlignCenter().Text((idx + 1).ToString()).FontSize(7).FontColor("#64748b");
                                table.Cell().Element(RStyle).Text(part.PartName).FontSize(8).Bold().FontColor("#0f172a");
                                table.Cell().Element(RStyle).AlignCenter().Text(part.Quantity.ToString()).FontSize(8);
                                table.Cell().Element(RStyle).AlignRight().Text(part.ChargeAmount.ToString("F2")).FontSize(8).Bold();
                            }
                        });
                    }

                    // Commercial Summary Box
                    col.Item().PaddingTop(10).Row(cr =>
                    {
                        cr.RelativeItem(5.5f).Column(termsCol =>
                        {
                            termsCol.Item().Text("Terms & Warranty:").FontSize(7.5f).Bold().FontColor("#334155");
                            termsCol.Item().Text("1. Device pickup requires physical receipt ticket or registered phone verification.").FontSize(7).FontColor("#64748b");
                            termsCol.Item().Text("2. Estimate may vary if unobserved liquid or board defects are discovered during repair.").FontSize(7).FontColor("#64748b");
                            termsCol.Item().Text("3. Backup data prior to service; shop is not responsible for unbacked user files.").FontSize(7).FontColor("#64748b");

                            if (dto.WarrantyUntil.HasValue)
                            {
                                termsCol.Item().PaddingTop(3).Text($"Warranty Valid Until: {dto.WarrantyUntil.Value:dd-MMM-yyyy}").FontSize(8).Bold().FontColor("#047857");
                            }
                        });

                        cr.ConstantItem(12);

                        cr.RelativeItem(4.5f).Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(6).Column(comCol =>
                        {
                            if (dto.EstimatedCost.HasValue)
                            {
                                comCol.Item().Row(r => { r.RelativeItem().Text("Initial Estimate:").FontSize(8).FontColor("#475569"); r.AutoItem().Text($"₹{dto.EstimatedCost.Value:F2}").FontSize(8).FontColor("#0f172a"); });
                            }
                            if (dto.AdvancePaid.HasValue)
                            {
                                comCol.Item().Row(r => { r.RelativeItem().Text("Advance Paid:").FontSize(8).FontColor("#475569"); r.AutoItem().Text($"₹{dto.AdvancePaid.Value:F2}").FontSize(8).FontColor("#047857"); });
                            }
                            if (dto.FinalTotal.HasValue)
                            {
                                comCol.Item().PaddingTop(2).LineHorizontal(1f).LineColor("#0f172a");
                                comCol.Item().PaddingTop(2).Row(r => { r.RelativeItem().Text("Final Total:").FontSize(10).Bold().FontColor("#0f172a"); r.AutoItem().Text($"₹{dto.FinalTotal.Value:F2}").FontSize(10).Bold().FontColor("#0f172a"); });
                            }
                            if (dto.BalanceDue.HasValue)
                            {
                                comCol.Item().Row(r => { r.RelativeItem().Text("Balance Due:").FontSize(8.5f).Bold().FontColor("#dc2626"); r.AutoItem().Text($"₹{dto.BalanceDue.Value:F2}").FontSize(8.5f).Bold().FontColor("#dc2626"); });
                            }
                            if (!string.IsNullOrWhiteSpace(dto.InvoiceNumber))
                            {
                                comCol.Item().PaddingTop(2).Text($"Billed on Invoice #{dto.InvoiceNumber}").FontSize(7.5f).Bold().FontColor("#2563eb");
                            }
                        });
                    });

                    // Signatures
                    col.Item().PaddingTop(12).Row(sRow =>
                    {
                        sRow.RelativeItem().Column(c =>
                        {
                            c.Item().Width(120).LineHorizontal(0.75f).LineColor("#94a3b8");
                            c.Item().PaddingTop(2).Text("Customer Acceptance").FontSize(7.5f).Bold().FontColor("#64748b");
                        });

                        sRow.AutoItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Width(140).LineHorizontal(0.75f).LineColor("#94a3b8");
                            c.Item().PaddingTop(2).AlignRight().Text($"For {dto.ShopName}").FontSize(7.5f).Bold().FontColor("#64748b");
                        });
                    });

                    // Technician Bench Stub (Cut-off strip)
                    col.Item().PaddingTop(14).Column(stub =>
                    {
                        stub.Item().LineHorizontal(0.75f).LineColor("#94a3b8");
                        stub.Item().PaddingTop(4).Row(sr =>
                        {
                            sr.RelativeItem().Text($"✂ Workshop Technician Bench Stub — #{dto.JobNumber}").FontSize(8).Bold().FontColor("#475569");
                            sr.AutoItem().Text($"Device: {dto.DeviceBrand} {dto.DeviceModel} | Customer: {dto.CustomerName}").FontSize(7.5f).FontColor("#64748b");
                        });
                    });
                });

                // ── Footer ────────────────────────────────────────────────────────
                page.Footer().AlignCenter().Row(fRow =>
                {
                    fRow.RelativeItem().AlignLeft().Text("StoreOS Service & Workshop Management").FontSize(7).FontColor("#94a3b8");
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
