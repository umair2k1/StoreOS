using System.Net;
using System.Net.Http.Headers;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Printing;

[Collection("postgres")]
public class PdfEndpointsTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid(); // Branch with no shop_profile setting
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private Guid _saleWithSettingId;
    private Guid _saleWithoutSettingId;
    private Guid _repairJobId;
    private Guid _supplierReturnId;

    public PdfEndpointsTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
        });

        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var passwordHash = _hasher.Hash("Password123!");

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        // 1. Tenant with repairs feature enabled
        var tenant = Tenant.Create("PDF Test Tenant", timezone: "Asia/Kolkata", subdomain: "pdf-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        tenant.SetFeatures("{\"repairs\": true}");
        db.Tenants.Add(tenant);

        // 2. Branches
        var branchA = Branch.Create(_tenantId, "PA", "Branch Alpha With Branding");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        branchA.UpdateProfile("Branch Alpha With Branding", "123 Alpha St", "080-11112222", "alpha@storeos.test", "29ABCDE1234F1Z5");
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "PB", "Branch Beta No Branding");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        branchB.UpdateProfile("Branch Beta No Branding", "456 Beta Ave", "080-33334444", "beta@storeos.test", "29VWXYZ5678G2Z3");
        db.Branches.Add(branchB);

        // 3. User & Role
        var user = User.Create(_tenantId, $"pdf-{_userId:N}@storeos.test", passwordHash, "Pdf User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var role = Role.Create(_tenantId, "Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        db.RolePermissions.Add(RolePermission.Create(_roleId, PermissionCatalog.PosCheckout));
        db.RolePermissions.Add(RolePermission.Create(_roleId, PermissionCatalog.PurchasesManage));
        db.RolePermissions.Add(RolePermission.Create(_roleId, PermissionCatalog.RepairsManage));
        db.RolePermissions.Add(RolePermission.Create(_roleId, PermissionCatalog.InventoryManage));

        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_userId, _roleId, _branchAId));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_userId, _roleId, _branchBId));
        db.UserBranches.Add(UserBranch.Create(_userId, _branchAId, isDefault: true));
        db.UserBranches.Add(UserBranch.Create(_userId, _branchBId, isDefault: false));

        // 4. Shop Profile Setting for Branch A ONLY
        var setting = Setting.Create(
            _tenantId,
            _branchAId,
            "shop_profile",
            "{\"shopName\":\"Custom Branding Electronics\",\"address\":\"789 Branding Blvd\",\"phone\":\"9998887776\",\"email\":\"custom@branding.test\",\"gstin\":\"29BRAND1234F1Z9\"}"
        );
        db.Settings.Add(setting);

        // 5. Seed Sale on Branch A (With shop_profile setting)
        var prodA = Product.Create(_tenantId, "Flagship Phone", ProductCategory.NewPhone, 50000m, 40000m, 18m, "8517");
        db.Products.Add(prodA);

        var saleA = Sale.CreateTaxInvoice(
            _tenantId, _branchAId, "INV-PDF-001", DateTimeOffset.UtcNow,
            customerId: null, customerName: "Rajesh Sharma", customerGstin: "29AABCU9603R1ZM",
            totalTaxable: 42372.88m, totalCgst: 3813.56m, totalSgst: 3813.56m, grandTotal: 50000m,
            discount: 0m, amountPaid: 50000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Sale A Description",
            repairJobId: null, createdBy: _userId);
        saleA.AddItem(SaleItem.Create(
            _tenantId, saleA.Id, prodA.Id, "Flagship Phone", ProductCategory.NewPhone,
            1, 50000m, 40000m, "8517", false, false, 42372.88m, 3813.56m, 3813.56m, "354455667788990"));
        db.Sales.Add(saleA);
        _saleWithSettingId = saleA.Id;

        // 6. Seed Sale on Branch B (NO shop_profile setting)
        var saleB = Sale.CreateTaxInvoice(
            _tenantId, _branchBId, "INV-PDF-002", DateTimeOffset.UtcNow,
            customerId: null, customerName: "Walk-in Guest", customerGstin: null,
            totalTaxable: 42372.88m, totalCgst: 3813.56m, totalSgst: 3813.56m, grandTotal: 50000m,
            discount: 0m, amountPaid: 50000m, paymentMode: PaymentMode.UPI,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Sale B Description",
            repairJobId: null, createdBy: _userId);
        saleB.AddItem(SaleItem.Create(
            _tenantId, saleB.Id, prodA.Id, "Flagship Phone", ProductCategory.NewPhone,
            1, 50000m, 40000m, "8517", false, false, 42372.88m, 3813.56m, 3813.56m, null));
        db.Sales.Add(saleB);
        _saleWithoutSettingId = saleB.Id;

        // 7. Seed Repair Job with a Part on Branch A
        var job = RepairJob.Intake(
            _tenantId, _branchAId, "REP-PDF-001",
            customerId: null, customerName: "Anita Rao", customerPhone: "9876501234", customerAddress: "Bangalore",
            deviceBrand: "Apple", deviceModel: "iPhone 13", deviceType: "Phone",
            serialNumber: "SN-IP13-001", imei1: "351122334455667", imei2: null,
            deviceCondition: "Scratched", accessoriesReceived: "None", problemDescription: "Battery drain",
            customerNotes: "Fast replacement requested");
        job.SetCommercials(estimatedCost: 5000m, approvedCost: 4500m, partsCost: 3500m, laborCharge: 1000m, discount: 0m, tax: 0m, finalTotal: 4500m, advancePaid: 1000m);
        db.RepairJobs.Add(job);
        _repairJobId = job.Id;

        var part = RepairJobPart.Reserve(_tenantId, job.Id, null, "OEM Battery Pack", 1, 2500m, 3500m);
        db.RepairJobParts.Add(part);

        // 8. Seed Supplier & Supplier Return on Branch A
        var supplier = Supplier.Create(_tenantId, "Supreme Parts Distributors", "080-99887766", "Industrial Area", "Spares", "Reliable partner");
        db.Suppliers.Add(supplier);

        var ret = SupplierReturn.Create(
            _tenantId, _branchAId, "RET-PDF-001", DateOnly.FromDateTime(DateTime.UtcNow),
            supplier.Id, "080-99887766", "Industrial Area", SettlementType.PendingCredit, "Damaged on arrival", "Return note notes", _userId);
        ret.AddItem(null, "Damaged Display Panel", 2, 3000m, 3000m, new[] { "359988776655441", "359988776655442" });
        db.SupplierReturns.Add(ret);
        _supplierReturnId = ret.Id;

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM supplier_return_items WHERE tenant_id = @tId; " +
            "DELETE FROM supplier_returns WHERE tenant_id = @tId; " +
            "DELETE FROM suppliers WHERE tenant_id = @tId; " +
            "DELETE FROM repair_job_parts WHERE tenant_id = @tId; " +
            "DELETE FROM repair_jobs WHERE tenant_id = @tId; " +
            "DELETE FROM sale_items WHERE tenant_id = @tId; " +
            "DELETE FROM sales WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM settings WHERE tenant_id = @tId; " +
            "DELETE FROM user_role_assignments WHERE user_id = @uId; " +
            "DELETE FROM user_branches WHERE user_id = @uId; " +
            "DELETE FROM role_permissions WHERE role_id = @rId; " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken()
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId,
            _tenantId,
            allBranches: true,
            branchIds: new[] { _branchAId, _branchBId },
            new[] { PermissionCatalog.PosCheckout, PermissionCatalog.PurchasesManage, PermissionCatalog.RepairsManage, PermissionCatalog.InventoryManage },
            tokenVersion: 1);
        return token;
    }

    private HttpClient CreateAuthenticatedClient(Guid branchId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken());
        client.DefaultRequestHeaders.Add(BranchContextMiddleware.HeaderName, branchId.ToString());
        return client;
    }

    [Fact]
    public async Task GetInvoicePdf_FullyPopulatedRecord_Returns200WithPdfContentTypeAndValidBytes()
    {
        // (a) GET /sales/{id}/invoice.pdf returns 200 with Content-Type: application/pdf and non-trivial byte length
        using var client = CreateAuthenticatedClient(_branchAId);

        var response = await client.GetAsync($"/api/sales/{_saleWithSettingId}/invoice.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes[..4]).Should().Be("%PDF");
    }

    [Fact]
    public async Task GetRepairTicketPdf_FullyPopulatedRecord_Returns200WithPdfContentTypeAndValidBytes()
    {
        // (a) GET /repair-jobs/{id}/ticket.pdf returns 200 with Content-Type: application/pdf and non-trivial byte length
        using var client = CreateAuthenticatedClient(_branchAId);

        var response = await client.GetAsync($"/api/repair-jobs/{_repairJobId}/ticket.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes[..4]).Should().Be("%PDF");
    }

    [Fact]
    public async Task GetSupplierReturnNotePdf_FullyPopulatedRecord_Returns200WithPdfContentTypeAndValidBytes()
    {
        // (a) GET /supplier-returns/{id}/note.pdf returns 200 with Content-Type: application/pdf and non-trivial byte length
        using var client = CreateAuthenticatedClient(_branchAId);

        var response = await client.GetAsync($"/api/supplier-returns/{_supplierReturnId}/note.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes[..4]).Should().Be("%PDF");
    }

    [Fact]
    public async Task GetInvoicePdf_WithoutShopProfileSetting_FallsBackGracefullyToBranchFields()
    {
        // (b) the invoice PDF endpoint works correctly even when NO "shop_profile" setting exists yet, falling back to branch fields without error
        using var client = CreateAuthenticatedClient(_branchBId);

        var response = await client.GetAsync($"/api/sales/{_saleWithoutSettingId}/invoice.pdf");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/pdf");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        Encoding.ASCII.GetString(bytes[..4]).Should().Be("%PDF");
    }

    [Fact]
    public async Task GetPdfEndpoints_NonExistentId_Returns404NotFound()
    {
        // (c) a nonexistent id on any of the three endpoints returns 404 (reusing each resource's existing not-found handling)
        using var client = CreateAuthenticatedClient(_branchAId);
        var nonExistentId = Guid.NewGuid();

        var saleRes = await client.GetAsync($"/api/sales/{nonExistentId}/invoice.pdf");
        saleRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var repairRes = await client.GetAsync($"/api/repair-jobs/{nonExistentId}/ticket.pdf");
        repairRes.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var returnRes = await client.GetAsync($"/api/supplier-returns/{nonExistentId}/note.pdf");
        returnRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
