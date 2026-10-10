using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace StoreOS.Infrastructure.Printing;

public class InvoicePdfRenderer
{
    public byte[] Render(InvoicePdfDto dto)
    {
        var isEstimate = string.Equals(dto.InvoiceType, "Estimate", StringComparison.OrdinalIgnoreCase);
        var docTitle = isEstimate ? "COMMERCIAL ESTIMATE" : "TAX INVOICE";

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
                        // Left: Shop Logo & Branding
                        row.RelativeItem().Row(brandRow =>
                        {
                            if (dto.LogoBytes is not null && dto.LogoBytes.Length > 0)
                            {
                                brandRow.AutoItem().PaddingRight(10).MaxWidth(75).MaxHeight(55).Image(dto.LogoBytes).FitArea();
                            }

                            brandRow.RelativeItem().Column(brandCol =>
                            {
                                brandCol.Item().Text(dto.ShopName)
                                    .FontSize(16).Bold().FontColor("#0f172a").LetterSpacing(0.04f);

                                if (!string.IsNullOrWhiteSpace(dto.ShopAddress))
                                {
                                    brandCol.Item().Text(dto.ShopAddress).FontSize(8).FontColor("#475569");
                                }

                                var contactParts = new List<string>();
                                if (!string.IsNullOrWhiteSpace(dto.ShopPhone)) contactParts.Add($"Phone: {dto.ShopPhone}");
                                if (!string.IsNullOrWhiteSpace(dto.ShopEmail)) contactParts.Add($"Email: {dto.ShopEmail}");

                                if (contactParts.Count > 0)
                                {
                                    brandCol.Item().Text(string.Join("  |  ", contactParts)).FontSize(8).FontColor("#475569");
                                }

                                if (!string.IsNullOrWhiteSpace(dto.ShopGstin) && !isEstimate)
                                {
                                    brandCol.Item().Text($"GSTIN: {dto.ShopGstin}").FontSize(8.5f).Bold().FontColor("#0f172a");
                                }
                            });
                        });

                        // Right: Document Metadata Card
                        row.AutoItem().Width(185).Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(6).Column(metaCol =>
                        {
                            metaCol.Item().AlignCenter().Text(docTitle).FontSize(11).Bold().FontColor("#0f172a");
                            metaCol.Item().PaddingTop(3).Row(r =>
                            {
                                r.RelativeItem().Text("Invoice #:").FontSize(8).FontColor("#64748b");
                                r.AutoItem().Text(dto.InvoiceNumber).FontSize(8.5f).Bold().FontColor("#0f172a");
                            });
                            metaCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Date:").FontSize(8).FontColor("#64748b");
                                r.AutoItem().Text($"{dto.SaleDate:dd/MM/yyyy HH:mm}").FontSize(8).FontColor("#0f172a");
                            });
                            metaCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text("Payment:").FontSize(8).FontColor("#64748b");
                                r.AutoItem().Text(dto.PaymentMode ?? "Cash").FontSize(8).Bold().FontColor("#0f172a");
                            });
                        });
                    });

                    // Bold divider
                    header.Item().PaddingTop(8).LineHorizontal(1.5f).LineColor("#0f172a");

                    // Customer details banner
                    header.Item().PaddingTop(6).PaddingBottom(6).Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(6).Row(custRow =>
                    {
                        custRow.RelativeItem().Column(c =>
                        {
                            c.Item().Text("BILLED TO / CUSTOMER:").FontSize(7.5f).Bold().FontColor("#64748b");
                            c.Item().Text(string.IsNullOrWhiteSpace(dto.CustomerName) ? "Walk-in Customer" : dto.CustomerName)
                                .FontSize(9.5f).Bold().FontColor("#0f172a");

                            if (!string.IsNullOrWhiteSpace(dto.CustomerPhone))
                            {
                                c.Item().Text($"Phone: {dto.CustomerPhone}").FontSize(8).FontColor("#475569");
                            }

                            if (!string.IsNullOrWhiteSpace(dto.CustomerGstin) && !isEstimate)
                            {
                                c.Item().Text($"Customer GSTIN: {dto.CustomerGstin}").FontSize(8).Bold().FontColor("#0f172a");
                            }
                        });

                        custRow.AutoItem().AlignBottom().Text("Supply Type: Retail / B2C").FontSize(7.5f).FontColor("#64748b");
                    });
                });

                // ── Line Items Table ──────────────────────────────────────────────
                page.Content().Column(content =>
                {
                    content.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(22);     // S.No
                            c.ConstantColumn(48);     // HSN
                            c.RelativeColumn(3.5f);   // Description
                            c.ConstantColumn(28);     // Qty
                            c.ConstantColumn(55);     // Rate
                            c.ConstantColumn(60);     // Taxable
                            c.ConstantColumn(46);     // CGST
                            c.ConstantColumn(46);     // SGST
                            c.ConstantColumn(62);     // Total
                        });

                        // Header Row
                        table.Header(h =>
                        {
                            static IContainer HeaderStyle(IContainer cell) =>
                                cell.Background("#f1f5f9").BorderBottom(1.5f).BorderColor("#64748b").PaddingVertical(4).PaddingHorizontal(3);

                            h.Cell().Element(HeaderStyle).AlignCenter().Text("#").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignCenter().Text("HSN").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).Text("Item Description").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignCenter().Text("Qty").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignRight().Text("Rate (₹)").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignRight().Text("Taxable (₹)").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignRight().Text("CGST (₹)").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignRight().Text("SGST (₹)").FontSize(7.5f).Bold().FontColor("#334155");
                            h.Cell().Element(HeaderStyle).AlignRight().Text("Total (₹)").FontSize(7.5f).Bold().FontColor("#334155");
                        });

                        // Line Rows
                        for (int idx = 0; idx < dto.Lines.Count; idx++)
                        {
                            var line = dto.Lines[idx];
                            var lineTotal = line.Taxable + line.Cgst + line.Sgst;
                            if (lineTotal == 0 && line.Qty > 0) lineTotal = line.Qty * line.PricePerUnit;

                            static IContainer RowStyle(IContainer cell) =>
                                cell.BorderBottom(0.5f).BorderColor("#e2e8f0").PaddingVertical(4).PaddingHorizontal(3);

                            table.Cell().Element(RowStyle).AlignCenter().Text((idx + 1).ToString()).FontSize(7.5f).FontColor("#64748b");
                            table.Cell().Element(RowStyle).AlignCenter().Text(line.Hsn ?? "—").FontSize(7.5f).FontColor("#64748b");

                            table.Cell().Element(RowStyle).Column(col =>
                            {
                                col.Item().Row(r =>
                                {
                                    r.RelativeItem().Text(line.ItemName).FontSize(8.5f).Bold().FontColor("#0f172a");
                                    if (line.IsGift)
                                    {
                                        r.AutoItem().Background("#ede9fe").PaddingHorizontal(3).PaddingVertical(1).Text("FREE GIFT").FontSize(6.5f).Bold().FontColor("#6d28d9");
                                    }
                                    if (line.IsMarginScheme)
                                    {
                                        r.AutoItem().PaddingLeft(3).Background("#fef3c7").PaddingHorizontal(3).PaddingVertical(1).Text("MARGIN SCHEME").FontSize(6.5f).Bold().FontColor("#b45309");
                                    }
                                });

                                if (!string.IsNullOrWhiteSpace(line.Imei))
                                {
                                    col.Item().Text($"IMEI / Serial: {line.Imei}").FontSize(7.5f).Italic().FontColor("#64748b");
                                }
                            });

                            table.Cell().Element(RowStyle).AlignCenter().Text(line.Qty.ToString()).FontSize(8).Bold();
                            table.Cell().Element(RowStyle).AlignRight().Text(line.PricePerUnit.ToString("F2")).FontSize(8);
                            table.Cell().Element(RowStyle).AlignRight().Text(line.Taxable.ToString("F2")).FontSize(8);
                            table.Cell().Element(RowStyle).AlignRight().Text(line.Cgst.ToString("F2")).FontSize(8);
                            table.Cell().Element(RowStyle).AlignRight().Text(line.Sgst.ToString("F2")).FontSize(8);
                            table.Cell().Element(RowStyle).AlignRight().Text(lineTotal.ToString("F2")).FontSize(8.5f).Bold().FontColor("#0f172a");
                        }
                    });

                    // ── Dual Bottom Column Section ────────────────────────────────────
                    content.Item().PaddingTop(10).Row(bottomRow =>
                    {
                        // Left: Financing Details & Amount in Words
                        bottomRow.RelativeItem(5.5f).Column(leftCol =>
                        {
                            leftCol.Item().Border(1).BorderColor("#e2e8f0").Background("#fafafa").Padding(6).Column(remarksBox =>
                            {
                                remarksBox.Item().Text("TRANSACTION & FINANCING DETAILS").FontSize(7.5f).Bold().FontColor("#475569");
                                remarksBox.Item().LineHorizontal(0.5f).LineColor("#e2e8f0");
                                remarksBox.Item().PaddingTop(3);

                                bool hasAnyMeta = false;
                                if (!string.IsNullOrWhiteSpace(dto.FinanceProvider))
                                {
                                    hasAnyMeta = true;
                                    var fn = !string.IsNullOrWhiteSpace(dto.FinancerName) ? $" ({dto.FinancerName})" : "";
                                    remarksBox.Item().Text($"Finance Partner: {dto.FinanceProvider}{fn}").FontSize(8).Bold().FontColor("#0f172a");
                                }

                                if (dto.DownPayment.HasValue && dto.DownPayment.Value > 0)
                                {
                                    hasAnyMeta = true;
                                    remarksBox.Item().Text($"Down Payment: ₹{dto.DownPayment.Value:F2}").FontSize(8).FontColor("#0f172a");
                                }

                                if (!string.IsNullOrWhiteSpace(dto.EmiDetails))
                                {
                                    hasAnyMeta = true;
                                    remarksBox.Item().Text($"EMI Terms: {dto.EmiDetails}").FontSize(8).FontColor("#475569");
                                }

                                if (!string.IsNullOrWhiteSpace(dto.Description))
                                {
                                    hasAnyMeta = true;
                                    remarksBox.Item().Text($"Remarks: {dto.Description}").FontSize(8).FontColor("#475569");
                                }

                                if (!hasAnyMeta)
                                {
                                    remarksBox.Item().Text("Standard Retail Sale").FontSize(8).Italic().FontColor("#64748b");
                                }
                            });

                            // Amount in Words
                            leftCol.Item().PaddingTop(6).Column(wCol =>
                            {
                                wCol.Item().Text("Amount in Words:").FontSize(7.5f).Bold().FontColor("#64748b");
                                wCol.Item().Text(IndianCurrencyWords.Convert(dto.GrandTotal)).FontSize(8.5f).Bold().Italic().FontColor("#0f172a");
                            });
                        });

                        bottomRow.ConstantItem(15); // Spacer

                        // Right: Financial Summary Totals Card
                        bottomRow.RelativeItem(4.5f).Border(1).BorderColor("#cbd5e1").Background("#f8fafc").Padding(6).Column(totalsCol =>
                        {
                            totalsCol.Item().Row(r =>
                            {
                                r.RelativeItem().Text(isEstimate ? "Subtotal:" : "Taxable Base:").FontSize(8).FontColor("#475569");
                                r.AutoItem().Text($"₹{dto.TotalTaxable:F2}").FontSize(8).FontColor("#0f172a");
                            });

                            if (!isEstimate)
                            {
                                totalsCol.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("CGST:").FontSize(8).FontColor("#475569");
                                    r.AutoItem().Text($"₹{dto.TotalCgst:F2}").FontSize(8).FontColor("#0f172a");
                                });

                                totalsCol.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("SGST:").FontSize(8).FontColor("#475569");
                                    r.AutoItem().Text($"₹{dto.TotalSgst:F2}").FontSize(8).FontColor("#0f172a");
                                });
                            }

                            if (dto.Discount > 0)
                            {
                                totalsCol.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Discount:").FontSize(8).Bold().FontColor("#dc2626");
                                    r.AutoItem().Text($"-₹{dto.Discount:F2}").FontSize(8).Bold().FontColor("#dc2626");
                                });
                            }

                            totalsCol.Item().PaddingTop(4).LineHorizontal(1.5f).LineColor("#0f172a");

                            totalsCol.Item().PaddingTop(3).Row(r =>
                            {
                                r.RelativeItem().Text("GRAND TOTAL:").FontSize(11).Bold().FontColor("#0f172a");
                                r.AutoItem().Text($"₹{dto.GrandTotal:F2}").FontSize(11).Bold().FontColor("#0f172a");
                            });

                            if (dto.Balance > 0 || string.Equals(dto.PaymentMode, "Credit", StringComparison.OrdinalIgnoreCase))
                            {
                                totalsCol.Item().PaddingTop(4).LineHorizontal(0.5f).LineColor("#cbd5e1");
                                totalsCol.Item().PaddingTop(2).Row(r =>
                                {
                                    r.RelativeItem().Text("Amount Paid:").FontSize(8).FontColor("#475569");
                                    r.AutoItem().Text($"₹{dto.AmountPaid:F2}").FontSize(8).FontColor("#0f172a");
                                });

                                totalsCol.Item().Row(r =>
                                {
                                    r.RelativeItem().Text("Balance Due:").FontSize(8.5f).Bold().FontColor("#dc2626");
                                    r.AutoItem().Text($"₹{dto.Balance:F2}").FontSize(8.5f).Bold().FontColor("#dc2626");
                                });
                            }
                        });
                    });

                    // ── Terms & Conditions & Signatures ──────────────────────────────
                    content.Item().PaddingTop(14).Row(sigRow =>
                    {
                        sigRow.RelativeItem(6).Column(termsCol =>
                        {
                            termsCol.Item().Text("Terms & Conditions:").FontSize(7.5f).Bold().FontColor("#334155");
                            termsCol.Item().Text("1. Goods once sold will not be returned or refunded.").FontSize(7.5f).FontColor("#475569");
                            termsCol.Item().Text("2. Delayed payments are subject to standard commercial interest.").FontSize(7.5f).FontColor("#475569");
                            termsCol.Item().Text("3. All disputes are subject to local judicial jurisdiction.").FontSize(7.5f).FontColor("#475569");

                            termsCol.Item().PaddingTop(4).Text(isEstimate
                                ? "This document is a commercial valuation estimate and does not represent an official tax ledger invoice."
                                : "We declare that this invoice shows the actual price of the goods described and that all particulars are true and correct.")
                                .FontSize(7).Italic().FontColor("#64748b");
                        });

                        sigRow.RelativeItem(4).AlignRight().Column(sigCol =>
                        {
                            sigCol.Item().AlignRight().Text($"For {dto.ShopName}").FontSize(8.5f).Bold().FontColor("#0f172a");
                            sigCol.Item().Height(30); // space for actual signature / physical stamp
                            sigCol.Item().AlignRight().Width(140).LineHorizontal(0.75f).LineColor("#94a3b8");
                            sigCol.Item().PaddingTop(2).AlignRight().Text("Authorised Signatory").FontSize(8).Bold().FontColor("#475569");
                        });
                    });
                });

                // ── Footer ────────────────────────────────────────────────────────
                page.Footer().AlignCenter().Row(fRow =>
                {
                    fRow.RelativeItem().AlignLeft().Text("This is a computer-generated invoice.").FontSize(7.5f).FontColor("#94a3b8");
                    fRow.RelativeItem().AlignRight().Text(x =>
                    {
                        x.Span("Page ").FontSize(7.5f).FontColor("#94a3b8");
                        x.CurrentPageNumber().FontSize(7.5f).FontColor("#94a3b8");
                        x.Span(" of ").FontSize(7.5f).FontColor("#94a3b8");
                        x.TotalPages().FontSize(7.5f).FontColor("#94a3b8");
                    });
                });
            });
        });

        return document.GeneratePdf();
    }
}
