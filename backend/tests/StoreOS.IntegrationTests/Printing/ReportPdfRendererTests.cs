using System.Text;
using FluentAssertions;
using QuestPDF.Infrastructure;
using StoreOS.Application.Reporting.DailySales;
using StoreOS.Application.Reporting.InventoryExport;
using StoreOS.Infrastructure.Printing;

namespace StoreOS.IntegrationTests.Printing;

public class ReportPdfRendererTests
{
    static ReportPdfRendererTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void RenderDailySales_FullData_ProducesValidPdf()
    {
        // (a) rendering a daily sales report with full seeded data succeeds and produces valid PDF bytes
        var renderer = new ReportPdfRenderer();
        var dto = new DailySalesReportDto(
            Date: DateOnly.FromDateTime(DateTime.UtcNow),
            Sales: new SalesSummaryDto(
                Revenue: 150000m,
                BuyingCost: 120000m,
                Profit: 30000m,
                GstCollected: 22881.36m,
                InvoiceCount: 5,
                ItemsSold: 7
            ),
            Inventory: new InventorySummaryDto(
                TotalStockVolume: 120,
                StockWorthAtCost: 850000m,
                RetailValuation: 1100000m,
                LowStockCount: 4,
                OutOfStockCount: 1
            ),
            Payments: new PaymentBreakdownDto(
                Cash: 50000m,
                Upi: 60000m,
                Finance: 40000m,
                Other: 0m,
                Total: 150000m
            ),
            ByCategory: new List<CategorySalesDto>
            {
                new("NewPhone", 100000m, 2),
                new("Accessory", 30000m, 4),
                new("RepairService", 20000m, 1)
            },
            ItemizedLog: new List<ItemizedSaleLineDto>
            {
                new("INV-001", DateOnly.FromDateTime(DateTime.UtcNow), "John Doe", "Galaxy S24", 1, 80000m, 65000m, 12203.39m, "354455667788990", "Cash"),
                new("INV-002", DateOnly.FromDateTime(DateTime.UtcNow), "Jane Smith", "Case Cover", 2, 2000m, 1200m, 305.08m, null, "UPI"),
                new("INV-003", DateOnly.FromDateTime(DateTime.UtcNow), "Walk-in", "AMOLED Screen Fix", 1, 20000m, 14000m, 3050.85m, null, "Finance")
            },
            CollectedAmount: 150000m,
            OutstandingAmount: 0m
        );

        var bytes = renderer.RenderDailySales(dto, "Super Mobile Central");

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(100);
        var magic = Encoding.ASCII.GetString(bytes[..4]);
        magic.Should().Be("%PDF");
    }

    [Fact]
    public void RenderInventoryExport_PhysicalAndServices_ProducesValidPdf()
    {
        // (b) rendering an inventory export with both physical and service rows succeeds
        var renderer = new ReportPdfRenderer();
        var dto = new InventoryExportDto(
            PhysicalItems: new List<PhysicalInventoryExportRowDto>
            {
                new("iPhone 15", "NewPhone", 15, 65000m, 75000m, 18m, "8517", "Apple India", "351122334455667 +14 more"),
                new("Type-C Cable", "Accessory", 50, 200m, 500m, 18m, "8544", "Cable Hub", null)
            },
            Services: new List<ServiceExportRowDto>
            {
                new("Display Replacement", "Original AMOLED panel replacement", 3500m, true, 18m, "9987"),
                new("Battery Swap", "Li-ion high capacity replacement", 1800m, true, 18m, "9987")
            }
        );

        var bytes = renderer.RenderInventoryExport(dto, "Super Mobile Central");

        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(100);
        var magic = Encoding.ASCII.GetString(bytes[..4]);
        magic.Should().Be("%PDF");
    }

    [Fact]
    public void Render_EmptyCollections_SucceedsWithoutThrowing()
    {
        // (c) rendering with EMPTY itemized logs / empty inventory lists (a quiet day/empty catalog) still succeeds without throwing
        var renderer = new ReportPdfRenderer();

        var emptyDailySales = new DailySalesReportDto(
            Date: DateOnly.FromDateTime(DateTime.UtcNow),
            Sales: new SalesSummaryDto(0m, 0m, 0m, 0m, 0, 0),
            Inventory: new InventorySummaryDto(0, 0m, 0m, 0, 0),
            Payments: new PaymentBreakdownDto(0m, 0m, 0m, 0m, 0m),
            ByCategory: Array.Empty<CategorySalesDto>(),
            ItemizedLog: Array.Empty<ItemizedSaleLineDto>(),
            CollectedAmount: 0m,
            OutstandingAmount: 0m
        );

        var dailySalesBytes = renderer.RenderDailySales(emptyDailySales);
        dailySalesBytes.Should().NotBeNull();
        dailySalesBytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(dailySalesBytes[..4]).Should().Be("%PDF");

        var emptyInventory = new InventoryExportDto(
            PhysicalItems: Array.Empty<PhysicalInventoryExportRowDto>(),
            Services: Array.Empty<ServiceExportRowDto>()
        );

        var inventoryBytes = renderer.RenderInventoryExport(emptyInventory);
        inventoryBytes.Should().NotBeNull();
        inventoryBytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(inventoryBytes[..4]).Should().Be("%PDF");
    }
}
