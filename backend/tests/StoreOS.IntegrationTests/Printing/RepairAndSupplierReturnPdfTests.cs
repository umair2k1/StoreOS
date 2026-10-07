using System.Text;
using FluentAssertions;
using QuestPDF.Infrastructure;
using StoreOS.Infrastructure.Printing;

namespace StoreOS.IntegrationTests.Printing;

public class RepairAndSupplierReturnPdfTests
{
    static RepairAndSupplierReturnPdfTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void RepairPdfRenderer_FullData_RendersValidPdf()
    {
        // (a) RepairPdfRenderer renders successfully with full data
        var renderer = new RepairPdfRenderer();
        var dto = new RepairPdfDto(
            ShopName: "QuickFix Repair Center",
            ShopAddress: "45 MG Road, Bangalore",
            ShopPhone: "080-22334455",
            JobNumber: "REP-2026-0042",
            ReceivedAt: DateTimeOffset.UtcNow.AddDays(-2),
            CustomerName: "Rajesh Kumar",
            CustomerPhone: "9876543210",
            DeviceBrand: "Samsung",
            DeviceModel: "Galaxy S23 Ultra",
            SerialNumber: "SN-S23-998877",
            Imei1: "354455667788990",
            ProblemDescription: "Cracked AMOLED screen and unresponsive touch",
            Status: "ReadyForPickup",
            EstimatedCost: 15000m,
            FinalTotal: 14500m,
            AdvancePaid: 2000m,
            BalanceDue: 12500m,
            WarrantyUntil: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(90)),
            Parts: new List<RepairPartLineDto>
            {
                new("AMOLED Display Assembly", 1, 12000m),
                new("Adhesive Tape Kit", 1, 500m),
                new("Screen Replacement Labor", 1, 2000m)
            },
            InvoiceNumber: "INV-2026-0899"
        );

        var bytes = renderer.Render(dto);

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(100);
        var magic = Encoding.ASCII.GetString(bytes[..4]);
        magic.Should().Be("%PDF");
    }

    [Fact]
    public void RepairPdfRenderer_AllOptionalFieldsNull_RendersValidPdf()
    {
        // (a) RepairPdfRenderer renders successfully with all-optional-fields-null (a job just received, no commercials yet)
        var renderer = new RepairPdfRenderer();
        var dto = new RepairPdfDto(
            ShopName: "QuickFix Repair Center",
            ShopAddress: null,
            ShopPhone: null,
            JobNumber: "REP-2026-0001",
            ReceivedAt: DateTimeOffset.UtcNow,
            CustomerName: "Walk-in Customer",
            CustomerPhone: null,
            DeviceBrand: null,
            DeviceModel: null,
            SerialNumber: null,
            Imei1: null,
            ProblemDescription: null,
            Status: "Received",
            EstimatedCost: null,
            FinalTotal: null,
            AdvancePaid: null,
            BalanceDue: null,
            WarrantyUntil: null,
            Parts: Array.Empty<RepairPartLineDto>(),
            InvoiceNumber: null
        );

        var bytes = renderer.Render(dto);

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(100);
        var magic = Encoding.ASCII.GetString(bytes[..4]);
        magic.Should().Be("%PDF");
    }

    [Fact]
    public void SupplierReturnPdfRenderer_MultipleLinesWithImeis_RendersValidPdf()
    {
        // (b) SupplierReturnPdfRenderer renders successfully with multiple lines including IMEI-bearing ones
        var renderer = new SupplierReturnPdfRenderer();
        var dto = new SupplierReturnPdfDto(
            ShopName: "Mega Electronics",
            ReturnNumber: "RET-2026-0015",
            ReturnDate: DateOnly.FromDateTime(DateTime.UtcNow),
            SupplierName: "Global Tech Distributors Ltd",
            SupplierPhone: "9988776655",
            SupplierAddress: "Sector 18, Electronic City",
            Lines: new List<SupplierReturnLineDto>
            {
                new(
                    ItemName: "Defective Smartphone X100",
                    Qty: 2,
                    ReturnRate: 25000m,
                    LineTotal: 50000m,
                    ImeiNumbers: new[] { "351122334455667", "351122334455668" }
                ),
                new(
                    ItemName: "Faulty USB-C Hub 7-in-1",
                    Qty: 5,
                    ReturnRate: 1200m,
                    LineTotal: 6000m,
                    ImeiNumbers: null
                ),
                new(
                    ItemName: "Damaged Earbuds Wireless",
                    Qty: 3,
                    ReturnRate: 3000m,
                    LineTotal: 9000m,
                    ImeiNumbers: new[] { "EB-101", "EB-102", "EB-103" }
                )
            },
            TotalAmount: 65000m,
            SettlementType: "CreditNote",
            Reason: "Batch manufacturing defect on screen assembly"
        );

        var bytes = renderer.Render(dto);

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(100);
        var magic = Encoding.ASCII.GetString(bytes[..4]);
        magic.Should().Be("%PDF");
    }
}
