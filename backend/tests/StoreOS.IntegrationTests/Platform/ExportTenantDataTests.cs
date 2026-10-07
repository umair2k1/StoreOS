using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform.BackupExport;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.FinanceOps;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Platform;

[Collection("postgres")]
public class ExportTenantDataTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _unprivilegedUserId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly Guid _unprivilegedRoleId = Guid.NewGuid();

    private Guid _saleAId;
    private Guid _saleBId;
    private Guid _productId;

    public ExportTenantDataTests(PostgresTestFixture fixture)
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
        var passwordHash = _hasher.Hash("SecretPassword123!");

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        // 1. Tenant
        var tenant = Tenant.Create("Export Test Tenant");
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        // 2. Branches
        var branchA = Branch.Create(_tenantId, "B01", "Branch Alpha");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "B02", "Branch Beta");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        // 3. Settings
        var setting = Setting.Create(_tenantId, _branchAId, "invoice.prefix", "\"INV-A\"");
        db.Settings.Add(setting);

        // 4. Roles & Permissions
        var role = Role.Create(_tenantId, "Tenant Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, PermissionCatalog.SettingsManage);
        db.RolePermissions.Add(perm);

        var unprivilegedRole = Role.Create(_tenantId, "Viewer");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(unprivilegedRole, _unprivilegedRoleId);
        db.Roles.Add(unprivilegedRole);

        var unprivPerm = RolePermission.Create(_unprivilegedRoleId, PermissionCatalog.InventoryManage);
        db.RolePermissions.Add(unprivPerm);

        // 5. Users
        var user = User.Create(_tenantId, "admin@exporttest.io", passwordHash, "Admin User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var unprivilegedUser = User.Create(_tenantId, "viewer@exporttest.io", passwordHash, "Viewer User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(unprivilegedUser, _unprivilegedUserId);
        db.Users.Add(unprivilegedUser);

        // 6. User Branches & Role Assignments
        var ubA = UserBranch.Create(_userId, _branchAId, isDefault: true);
        db.UserBranches.Add(ubA);

        var ura = UserRoleAssignment.Create(_userId, _roleId, _branchAId);
        db.UserRoleAssignments.Add(ura);

        var unprivUb = UserBranch.Create(_unprivilegedUserId, _branchAId, isDefault: true);
        db.UserBranches.Add(unprivUb);

        var unprivUra = UserRoleAssignment.Create(_unprivilegedUserId, _unprivilegedRoleId, _branchAId);
        db.UserRoleAssignments.Add(unprivUra);

        // 7. Product & Inventories
        var product = Product.Create(_tenantId, "Test Phone 15", ProductCategory.NewPhone, 60000m, 45000m, 18m, "85171200");
        db.Products.Add(product);
        _productId = product.Id;

        var invA = BranchInventory.Create(_tenantId, _branchAId, product.Id);
        invA.IncreaseStock(10, 45000m);
        db.BranchInventories.Add(invA);

        var invB = BranchInventory.Create(_tenantId, _branchBId, product.Id);
        invB.IncreaseStock(5, 45000m);
        db.BranchInventories.Add(invB);

        // 8. Customer
        var customer = Customer.Create(_tenantId, "Customer Alice", "9876543210", null);
        db.Customers.Add(customer);

        // 9. Sales across MULTIPLE branches (Branch A and Branch B)
        var saleA = Sale.CreateTaxInvoice(
            _tenantId, _branchAId, "INV-A-001", DateTimeOffset.UtcNow,
            customer.Id, customer.Name, null,
            60000m, 5400m, 5400m, 70800m, 0m, 70800m,
            "Cash", null, null, null, null, null, null, null,
            _userId);
        var itemA = SaleItem.Create(_tenantId, saleA.Id, product.Id, product.Name, product.Category, 1, 60000m, 45000m, "85171200", false, false, 60000m, 5400m, 5400m, "350000000000001");
        saleA.AddItem(itemA);
        db.Sales.Add(saleA);
        _saleAId = saleA.Id;

        var saleB = Sale.CreateTaxInvoice(
            _tenantId, _branchBId, "INV-B-001", DateTimeOffset.UtcNow,
            customer.Id, customer.Name, null,
            60000m, 5400m, 5400m, 70800m, 0m, 70800m,
            "UPI", null, null, null, null, null, null, null,
            _userId);
        var itemB = SaleItem.Create(_tenantId, saleB.Id, product.Id, product.Name, product.Category, 1, 60000m, 45000m, "85171200", false, false, 60000m, 5400m, 5400m, "350000000000002");
        saleB.AddItem(itemB);
        db.Sales.Add(saleB);
        _saleBId = saleB.Id;

        // 10. Customer Payment
        var paymentA = CustomerPayment.Create(_tenantId, saleA.Id, 70800m, "Cash", DateOnly.FromDateTime(DateTime.UtcNow), _userId);
        db.CustomerPayments.Add(paymentA);

        // 11. Expense
        var expA = Expense.Create(_tenantId, _branchAId, "Office Supplies Branch A", 500m, "Supplies", DateOnly.FromDateTime(DateTime.UtcNow), null, _userId);
        var expB = Expense.Create(_tenantId, _branchBId, "Office Supplies Branch B", 750m, "Supplies", DateOnly.FromDateTime(DateTime.UtcNow), null, _userId);
        db.Expenses.AddRange(expA, expB);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM customer_payments WHERE tenant_id = @tId; " +
            "DELETE FROM sale_items WHERE tenant_id = @tId; " +
            "DELETE FROM sales WHERE tenant_id = @tId; " +
            "DELETE FROM expenses WHERE tenant_id = @tId; " +
            "DELETE FROM customers WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM settings WHERE tenant_id = @tId; " +
            "DELETE FROM user_role_assignments WHERE user_id = @uId OR user_id = @unprivUId; " +
            "DELETE FROM user_branches WHERE user_id = @uId OR user_id = @unprivUId; " +
            "DELETE FROM role_permissions WHERE role_id = @rId OR role_id = @unprivRId; " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);

        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("unprivUId", _unprivilegedUserId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("unprivRId", _unprivilegedRoleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, Guid roleId, bool hasSettingsManage, Guid branchId)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var permissions = hasSettingsManage
            ? new[] { PermissionCatalog.SettingsManage }
            : new[] { PermissionCatalog.InventoryManage };

        // Exporting user's own token scope is STRICTLY restricted to branchId (not allBranches)
        var (token, _) = jwtService.IssueAccessToken(
            userId,
            _tenantId,
            allBranches: false,
            branchIds: new[] { branchId },
            permissions,
            tokenVersion: 1);

        return token;
    }

    private HttpClient CreateClientForUser(Guid userId, Guid roleId, bool hasSettingsManage, Guid branchId)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(userId, roleId, hasSettingsManage, branchId));
        client.DefaultRequestHeaders.Add(BranchContextMiddleware.HeaderName, branchId.ToString());
        return client;
    }

    [Fact]
    public async Task Export_WithMultiBranchDataAndSingleBranchScopedUser_ReturnsAllBranchesAndFullDto()
    {
        // (a) exporting user's branch scope is ONLY Branch A, but data exists across Branch A and Branch B
        using var client = CreateClientForUser(_userId, _roleId, hasSettingsManage: true, _branchAId);

        var response = await client.PostAsync("/api/tenant/export", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // (b) SchemaVersion is present and "1.0"
        root.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        root.GetProperty("tenantId").GetGuid().Should().Be(_tenantId);

        // (a) Prove IgnoreQueryFilters() exported ALL branches despite caller only having Branch A
        var branches = root.GetProperty("branches");
        branches.GetArrayLength().Should().Be(2);

        var branchInventories = root.GetProperty("branchInventories");
        branchInventories.GetArrayLength().Should().Be(2);

        var sales = root.GetProperty("sales");
        sales.GetArrayLength().Should().Be(2);

        var expenses = root.GetProperty("expenses");
        expenses.GetArrayLength().Should().Be(2);

        // (c) password_hash values are present and Argon2 formatted, NEVER plaintext
        var users = root.GetProperty("users");
        users.GetArrayLength().Should().BeGreaterThanOrEqualTo(2);
        var firstUser = users[0];

        string? passwordHash = null;
        if (firstUser.TryGetProperty("password_hash", out var propA))
            passwordHash = propA.GetString();
        else if (firstUser.TryGetProperty("passwordHash", out var propB))
            passwordHash = propB.GetString();

        passwordHash.Should().NotBeNullOrWhiteSpace();
        passwordHash!.Should().StartWith("argon2id$");
        passwordHash.Should().NotBe("SecretPassword123!");

        // (d) confirm a file was uploaded to MinIO bucket and compare its content
        using var scope = _factory.Services.CreateScope();
        var s3Client = scope.ServiceProvider.GetRequiredService<IAmazonS3>();

        var listResponse = await s3Client.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = "storeos-backups",
            Prefix = $"{_tenantId}/"
        });

        listResponse.S3Objects.Should().NotBeEmpty();
        var uploadedObject = listResponse.S3Objects.OrderByDescending(o => o.LastModified).First();

        var getResponse = await s3Client.GetObjectAsync(new GetObjectRequest
        {
            BucketName = "storeos-backups",
            Key = uploadedObject.Key
        });

        using var reader = new StreamReader(getResponse.ResponseStream);
        var s3Json = await reader.ReadToEndAsync();
        using var s3Doc = JsonDocument.Parse(s3Json);
        s3Doc.RootElement.GetProperty("schemaVersion").GetString().Should().Be("1.0");
        s3Doc.RootElement.GetProperty("tenantId").GetGuid().Should().Be(_tenantId);
    }

    [Fact]
    public async Task Export_WithoutSettingsManagePermission_Returns403()
    {
        // (e) caller without settings:manage -> 403
        using var client = CreateClientForUser(_unprivilegedUserId, _unprivilegedRoleId, hasSettingsManage: false, _branchAId);

        var response = await client.PostAsync("/api/tenant/export", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MinioObjectStorageService_RoundTrip_UploadAndGetAndMissingReturnsNull()
    {
        // Round trip test against MinIO container: upload, get, get-missing => null
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IObjectStorageService>();

        var testBucket = "storeos-backups";
        var testKey = $"test-roundtrip/{Guid.NewGuid()}.txt";
        var content = System.Text.Encoding.UTF8.GetBytes("Hello StoreOS MinIO Roundtrip!");

        // 1. Upload
        var uploadResult = await storage.UploadAsync(testBucket, testKey, content, "text/plain", CancellationToken.None);
        uploadResult.Should().Be($"{testBucket}/{testKey}");

        // 2. Get existing
        var stored = await storage.GetAsync(testBucket, testKey, CancellationToken.None);
        stored.Should().NotBeNull();
        stored!.ContentType.Should().Be("text/plain");
        System.Text.Encoding.UTF8.GetString(stored.Content).Should().Be("Hello StoreOS MinIO Roundtrip!");

        // 3. Get missing returns null
        var missing = await storage.GetAsync(testBucket, $"non-existent-key-{Guid.NewGuid()}", CancellationToken.None);
        missing.Should().BeNull();
    }
}
