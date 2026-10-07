using System.Text;
using FluentAssertions;
using QuestPDF.Infrastructure;
using StoreOS.Infrastructure.Printing;

namespace StoreOS.IntegrationTests.Printing;

public class InvoicePdfRendererTests
{
    static InvoicePdfRendererTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void Render_WellFormedDto_ReturnsValidPdfBytes()
    {
        // (a) rendering a well-formed InvoicePdfDto returns a non-empty byte array starting with the PDF magic bytes (%PDF)
        var renderer = new InvoicePdfRenderer();
        var dto = new InvoicePdfDto(
            ShopName: "Super Mobile Hub",
            ShopAddress: "123 Main Street, Bangalore",
            ShopPhone: "9876543210",
            ShopEmail: "contact@supermobile.com",
            ShopGstin: "29ABCDE1234F1Z5",
            InvoiceNumber: "INV-2026-001",
            InvoiceType: "Tax Invoice",
            SaleDate: DateTimeOffset.UtcNow,
            CustomerName: "John Doe",
            CustomerGstin: "29AABCU9603R1ZM",
            Lines: new List<InvoicePdfLineDto>
            {
                new("iPhone 15 Pro", "8517", 1, 120000m, 101694.92m, 9152.54m, 9152.54m, "356789012345678", false)
            },
            TotalTaxable: 101694.92m,
            TotalCgst: 9152.54m,
            TotalSgst: 9152.54m,
            Discount: 0m,
            GrandTotal: 120000m,
            AmountPaid: 120000m,
            Balance: 0m,
            PaymentMode: "Cash",
            FinanceProvider: "Bajaj Finserv",
            FinancerName: "Bajaj",
            Description: "Retail Sale",
            IrnDisplay: null,
            IrnQrDataUrl: null
        );

        var bytes = renderer.Render(dto);

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(100);
        var header = Encoding.ASCII.GetString(bytes[..4]);
        header.Should().Be("%PDF");
    }

    [Fact]
    public void Render_EveryOptionalFieldNull_Succeeds()
    {
        // (b) rendering succeeds with EVERY optional field null (minimal walk-in cash sale, no GSTIN, no finance, no IRN)
        var renderer = new InvoicePdfRenderer();
        var dto = new InvoicePdfDto(
            ShopName: "Quick Mart",
            ShopAddress: null,
            ShopPhone: null,
            ShopEmail: null,
            ShopGstin: null,
            InvoiceNumber: "INV-0002",
            InvoiceType: "Invoice",
            SaleDate: DateTimeOffset.UtcNow,
            CustomerName: "Walk-in Customer",
            CustomerGstin: null,
            Lines: new List<InvoicePdfLineDto>
            {
                new("Screen Protector", null, 1, 100m, 84.75m, 7.625m, 7.625m, null, false)
            },
            TotalTaxable: 84.75m,
            TotalCgst: 7.625m,
            TotalSgst: 7.625m,
            Discount: 0m,
            GrandTotal: 100m,
            AmountPaid: 100m,
            Balance: 0m,
            PaymentMode: null,
            FinanceProvider: null,
            FinancerName: null,
            Description: null,
            IrnDisplay: null,
            IrnQrDataUrl: null
        );

        var bytes = renderer.Render(dto);

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(100);
        var header = Encoding.ASCII.GetString(bytes[..4]);
        header.Should().Be("%PDF");
    }

    [Fact]
    public void Render_GiftLineAndImeiLine_ProducesNonTrivialByteLengthComparedToSingleLine()
    {
        // (c) rendering with a gift line and an IMEI line both display their respective annotations correctly
        // (at minimum assert the render does not throw and produces a plausible non-trivial byte length
        // for a multi-line invoice vs. a single-line one)
        var renderer = new InvoicePdfRenderer();

        var singleLineDto = new InvoicePdfDto(
            ShopName: "Mobile Planet",
            ShopAddress: null,
            ShopPhone: null,
            ShopEmail: null,
            ShopGstin: null,
            InvoiceNumber: "INV-0003",
            InvoiceType: "Tax Invoice",
            SaleDate: DateTimeOffset.UtcNow,
            CustomerName: "Customer Single",
            CustomerGstin: null,
            Lines: new List<InvoicePdfLineDto>
            {
                new("Phone Case", "3926", 1, 500m, 423.73m, 38.135m, 38.135m, null, false)
            },
            TotalTaxable: 423.73m,
            TotalCgst: 38.135m,
            TotalSgst: 38.135m,
            Discount: 0m,
            GrandTotal: 500m,
            AmountPaid: 500m,
            Balance: 0m,
            PaymentMode: "Cash",
            FinanceProvider: null,
            FinancerName: null,
            Description: null,
            IrnDisplay: null,
            IrnQrDataUrl: null
        );

        var multiLineDto = new InvoicePdfDto(
            ShopName: "Mobile Planet",
            ShopAddress: "Koramangala, Bangalore",
            ShopPhone: "9876543210",
            ShopEmail: "info@mobileplanet.com",
            ShopGstin: "29ABCDE1234F1Z5",
            InvoiceNumber: "INV-0004",
            InvoiceType: "Tax Invoice",
            SaleDate: DateTimeOffset.UtcNow,
            CustomerName: "Customer Multi",
            CustomerGstin: "29AABCU9603R1ZM",
            Lines: new List<InvoicePdfLineDto>
            {
                new("Flagship Phone", "8517", 1, 80000m, 67796.61m, 6101.695m, 6101.695m, "990011223344556", false),
                new("Free Bluetooth Headset", "8518", 1, 0m, 0m, 0m, 0m, null, true),
                new("Adapter Fast Charger", "8504", 1, 2000m, 1694.92m, 152.54m, 152.54m, null, false)
            },
            TotalTaxable: 69491.53m,
            TotalCgst: 6254.235m,
            TotalSgst: 6254.235m,
            Discount: 0m,
            GrandTotal: 82000m,
            AmountPaid: 82000m,
            Balance: 0m,
            PaymentMode: "Card",
            FinanceProvider: null,
            FinancerName: null,
            Description: null,
            IrnDisplay: null,
            IrnQrDataUrl: null
        );

        var singleBytes = renderer.Render(singleLineDto);
        var multiBytes = renderer.Render(multiLineDto);

        singleBytes.Should().NotBeNull();
        multiBytes.Should().NotBeNull();
        Encoding.ASCII.GetString(singleBytes[..4]).Should().Be("%PDF");
        Encoding.ASCII.GetString(multiBytes[..4]).Should().Be("%PDF");

        // Multi-line invoice with annotations produces a valid PDF
        multiBytes.Length.Should().BeGreaterThan(singleBytes.Length);
    }
}
