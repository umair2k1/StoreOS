using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Purchasing.ImportSupplierInvoice;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Platform;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Purchasing;

[Collection("postgres")]
public class SupplierInvoiceImportTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private Guid _supplierId;
    private Guid _singleMatchProdId;
    private Guid _dupProd1Id;
    private Guid _dupProd2Id;

    public SupplierInvoiceImportTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Import Test Tenant", subdomain: "imp-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "IP1", "Import HQ");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var supplier = Supplier.Create(_tenantId, "National Wholesaler", "9998887770", "Wholesale Hub", "Electronics", null);
        db.Suppliers.Add(supplier);
        _supplierId = supplier.Id;

        // Exactly one product named "Unique USB Cable"
        var pSingle = Product.Create(_tenantId, "Unique USB Cable", ProductCategory.Accessory, 500m, 200m, 18m, "85444299");
        db.Products.Add(pSingle);
        _singleMatchProdId = pSingle.Id;

        // Two products with same name "Dual Match Adapter" in different categories
        var pDup1 = Product.Create(_tenantId, "Dual Match Adapter", ProductCategory.Accessory, 300m, 100m, 18m, null);
        var pDup2 = Product.Create(_tenantId, "Dual Match Adapter", ProductCategory.UsedPhone, 400m, 150m, 18m, null);
        db.Products.Add(pDup1);
        db.Products.Add(pDup2);
        _dupProd1Id = pDup1.Id;
        _dupProd2Id = pDup2.Id;

        var role = Role.Create(_tenantId, "PurchasingAgent");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "purchases:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "importer@test.com", passwordHash, "Import User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var ub = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@uId, @rId, NULL);", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id = @uId; " +
            "DELETE FROM user_branches WHERE user_id = @uId; " +
            "DELETE FROM users WHERE id = @uId; " +
            "DELETE FROM role_permissions WHERE role_id = @rId; " +
            "DELETE FROM roles WHERE id = @rId; " +
            "DELETE FROM import_batches WHERE tenant_id = @tId; " +
            "DELETE FROM inventory_movements WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM purchases WHERE tenant_id = @tId; " +
            "DELETE FROM suppliers WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id = @bId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken()
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: true, Array.Empty<Guid>(), new[] { "purchases:manage" }, tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task Import_AllLinesMatchingExactlyOneProduct_ReturnsImported_AndIncreasesStock()
    {
        var token = IssueToken();

        var lines = new[]
        {
            new SupplierInvoiceImportLine("unique usb cable", "85444299", 15, 180m, null)
        };

        var command = new ImportSupplierInvoiceCommand(
            SupplierId: _supplierId,
            SupplierInvoiceNo: "INV-SINGLE-01",
            InvoiceDate: new DateOnly(2026, 10, 5),
            SourceRef: "file-hash-all-match-123",
            Lines: lines);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/imports/supplier-invoice");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        result.Should().NotBeNull();
        result!.Status.Should().Be(ImportStatus.Imported);
        result.ProductsMatched.Should().Be(1);
        result.ProductsAmbiguous.Should().Be(0);
        result.PurchasesCreated.Should().Be(1);
        result.AmbiguousLines.Should().BeEmpty();

        // Verify stock is 15 in database
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var inv = await db.BranchInventories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _singleMatchProdId);
        inv.Should().NotBeNull();
        inv!.StockQty.Should().Be(15);
    }

    [Fact]
    public async Task Import_PartialSuccessWithZeroAndMultipleMatches_ReturnsPending_AndProcessesUnambiguousLines()
    {
        var token = IssueToken();

        var lines = new[]
        {
            new SupplierInvoiceImportLine("unique usb cable", "85444299", 10, 180m, null), // exact match
            new SupplierInvoiceImportLine("dual match adapter", null, 5, 120m, null),       // 2 candidate matches
            new SupplierInvoiceImportLine("non existent product xyz", null, 2, 50m, null)   // 0 candidate matches
        };

        var command = new ImportSupplierInvoiceCommand(
            SupplierId: _supplierId,
            SupplierInvoiceNo: "INV-PARTIAL-02",
            InvoiceDate: new DateOnly(2026, 10, 5),
            SourceRef: "file-hash-partial-456",
            Lines: lines);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/imports/supplier-invoice");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ImportResult>();
        result.Should().NotBeNull();
        result!.Status.Should().Be(ImportStatus.Pending);
        result.ProductsMatched.Should().Be(1);
        result.ProductsAmbiguous.Should().Be(2);
        result.PurchasesCreated.Should().Be(1);

        result.AmbiguousLines.Should().HaveCount(2);

        // Ambiguous line 1: non-existent product -> empty candidate list
        var zeroMatchLine = result.AmbiguousLines.FirstOrDefault(l => l.ProductNameRaw == "non existent product xyz");
        zeroMatchLine.Should().NotBeNull();
        zeroMatchLine!.CandidateProductIds.Should().BeEmpty();

        // Ambiguous line 2: dual match -> 2 candidates
        var multiMatchLine = result.AmbiguousLines.FirstOrDefault(l => l.ProductNameRaw == "dual match adapter");
        multiMatchLine.Should().NotBeNull();
        multiMatchLine!.CandidateProductIds.Should().HaveCount(2);
        multiMatchLine.CandidateProductIds.Should().Contain(new[] { _dupProd1Id, _dupProd2Id });

        // Unambiguous line was processed
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var inv = await db.BranchInventories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _singleMatchProdId);
        inv.Should().NotBeNull();
        inv!.StockQty.Should().BeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public async Task Import_ExactSameSourceRefTwice_ReturnsExistingResultWithoutCreatingDuplicateStock()
    {
        var token = IssueToken();

        const string sourceRef = "file-hash-dedup-789";

        var lines = new[]
        {
            new SupplierInvoiceImportLine("unique usb cable", "85444299", 5, 180m, null)
        };

        var command = new ImportSupplierInvoiceCommand(
            SupplierId: _supplierId,
            SupplierInvoiceNo: "INV-DEDUP-03",
            InvoiceDate: new DateOnly(2026, 10, 5),
            SourceRef: sourceRef,
            Lines: lines);

        // 1st Import
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/imports/supplier-invoice");
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req1.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        req1.Content = JsonContent.Create(command);

        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var result1 = await res1.Content.ReadFromJsonAsync<ImportResult>();
        result1!.Status.Should().Be(ImportStatus.Imported);

        // Read initial purchase count for this invoice no
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        int initialPurchases;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            initialPurchases = await db.Purchases.IgnoreQueryFilters()
                .CountAsync(p => p.TenantId == _tenantId && p.SupplierInvoiceNo == "INV-DEDUP-03");
        }
        initialPurchases.Should().Be(1);

        // 2nd Import with identical sourceRef
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/imports/supplier-invoice");
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req2.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        req2.Content = JsonContent.Create(command);

        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        var result2 = await res2.Content.ReadFromJsonAsync<ImportResult>();
        result2!.ImportBatchId.Should().Be(result1.ImportBatchId);
        result2.Status.Should().Be(ImportStatus.Imported);

        // Verify NO duplicate purchase rows were created
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var finalPurchases = await db.Purchases.IgnoreQueryFilters()
                .CountAsync(p => p.TenantId == _tenantId && p.SupplierInvoiceNo == "INV-DEDUP-03");
            finalPurchases.Should().Be(initialPurchases);
        }
    }
}
