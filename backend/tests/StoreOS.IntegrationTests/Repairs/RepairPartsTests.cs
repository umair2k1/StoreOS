using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Controllers;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Repairs.Jobs;
using StoreOS.Application.Repairs.Jobs.CreateRepairJob;
using StoreOS.Application.Repairs.Parts;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Repairs;

[Collection("postgres")]
public class RepairPartsTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private const string BranchCode = "B1";

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private readonly Guid _productId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RepairPartsTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Repair Parts Test Tenant", subdomain: "rpt-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        tenant.SetFeatures("{\"repairs\":true}");
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, BranchCode, "Branch 1");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "RepairPartsMgrRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "repairs:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "partsmgr@test.com", passwordHash, "Parts Mgr");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var ub = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub);

        // Seed a tracked product (Accessory / screen part) with initial stock 5
        var product = Product.Create(_tenantId, "iPhone 14 OEM Screen", ProductCategory.Accessory, 4500m, 3000m, 18m, "851770");
        typeof(Product).GetProperty(nameof(Product.Id))!.SetValue(product, _productId);
        db.Products.Add(product);

        var inv = BranchInventory.Create(_tenantId, _branchId, _productId);
        inv.IncreaseStock(5, 3000m);
        db.BranchInventories.Add(inv);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@u, @r, NULL);", conn);
        cmd.Parameters.AddWithValue("u", _userId);
        cmd.Parameters.AddWithValue("r", _roleId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id = @u; " +
            "DELETE FROM user_branches WHERE user_id = @u; " +
            "DELETE FROM users WHERE id = @u; " +
            "DELETE FROM role_permissions WHERE role_id = @r; " +
            "DELETE FROM roles WHERE id = @r; " +
            "DELETE FROM repair_job_parts WHERE tenant_id = @t; " +
            "DELETE FROM repair_job_status_history WHERE tenant_id = @t; " +
            "DELETE FROM repair_jobs WHERE tenant_id = @t; " +
            "DELETE FROM inventory_movements WHERE tenant_id = @t; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @t; " +
            "DELETE FROM products WHERE tenant_id = @t; " +
            "DELETE FROM invoice_sequences WHERE tenant_id = @t; " +
            "DELETE FROM branches WHERE id = @b; " +
            "DELETE FROM tenants WHERE id = @t;", conn);
        cmd.Parameters.AddWithValue("u", _userId);
        cmd.Parameters.AddWithValue("r", _roleId);
        cmd.Parameters.AddWithValue("t", _tenantId);
        cmd.Parameters.AddWithValue("b", _branchId);
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
            Array.Empty<Guid>(),
            new[] { "repairs:manage" },
            tokenVersion: 1);
        return token;
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken());
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());
        return client;
    }

    private async Task<Guid> CreateJobAsync(HttpClient client)
    {
        var res = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Customer Parts Test",
            CustomerPhone: "9876543210",
            CustomerAddress: null,
            DeviceBrand: "Apple",
            DeviceModel: "iPhone 14",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: null,
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "Broken screen",
            CustomerNotes: null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetGuid();
    }

    private async Task<int> GetCurrentStockAsync(Guid productId)
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var inv = await db.BranchInventories
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.TenantId == _tenantId && b.BranchId == _branchId && b.ProductId == productId);
        return inv?.StockQty ?? 0;
    }

    [Fact]
    public async Task ReserveTrackedPart_DoesNotDecrementStockYet()
    {
        // (a) reserving a tracked part does NOT decrement stock yet
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        var initialStock = await GetCurrentStockAsync(_productId);
        initialStock.Should().Be(5);

        // Reserve 2 screens
        var res = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/parts", new ReserveRepairPartBody(
            ProductId: _productId,
            PartName: "iPhone 14 OEM Screen",
            Quantity: 2,
            UnitCost: 3000m,
            ChargeAmount: 4500m));
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var partId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Verify part is reserved
        var parts = await client.GetFromJsonAsync<IReadOnlyList<RepairJobPartDto>>($"/api/repair-jobs/{jobId}/parts", JsonOpts);
        parts.Should().NotBeNull();
        parts!.Should().Contain(p => p.Id == partId && p.Status == RepairPartStatus.Reserved);

        // Stock in DB should remain unchanged (5)
        var stockAfterReserve = await GetCurrentStockAsync(_productId);
        stockAfterReserve.Should().Be(5);
    }

    [Fact]
    public async Task ConsumeTrackedPart_DecrementsStock_AndFailsAtomicallyIfInsufficient()
    {
        // (b) consuming it DOES decrement stock atomically, and fails with insufficient_stock if not enough is available
        // (whole operation rolls back, part stays Reserved)
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        // 1. Reserve 2 screens and consume them
        var res1 = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/parts", new ReserveRepairPartBody(
            ProductId: _productId,
            PartName: "iPhone 14 OEM Screen",
            Quantity: 2,
            UnitCost: 3000m,
            ChargeAmount: 4500m));
        var part1Id = (await res1.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var consumeRes1 = await client.PostAsync($"/api/repair-jobs/{jobId}/parts/{part1Id}/consume", null);
        consumeRes1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Stock should now be 5 - 2 = 3
        var stockAfterConsume = await GetCurrentStockAsync(_productId);
        stockAfterConsume.Should().Be(3);

        // 2. Reserve 10 screens (more than available 3)
        var res2 = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/parts", new ReserveRepairPartBody(
            ProductId: _productId,
            PartName: "iPhone 14 OEM Screen",
            Quantity: 10,
            UnitCost: 3000m,
            ChargeAmount: 4500m));
        var part2Id = (await res2.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Attempting to consume 10 should fail with insufficient_stock (422)
        var consumeRes2 = await client.PostAsync($"/api/repair-jobs/{jobId}/parts/{part2Id}/consume", null);
        consumeRes2.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Part 2 should remain Reserved
        var parts = await client.GetFromJsonAsync<IReadOnlyList<RepairJobPartDto>>($"/api/repair-jobs/{jobId}/parts", JsonOpts);
        parts.Should().NotBeNull();
        var part2 = parts!.First(p => p.Id == part2Id);
        part2.Status.Should().Be(RepairPartStatus.Reserved);

        // Stock should remain 3
        var stockAfterFail = await GetCurrentStockAsync(_productId);
        stockAfterFail.Should().Be(3);
    }

    [Fact]
    public async Task ReturnConsumedPart_CorrectlyIncreasesStockBack()
    {
        // (c) returning a Consumed part correctly INCREASES stock back
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        // Reserve 2 and consume -> stock goes 5 -> 3
        var res = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/parts", new ReserveRepairPartBody(
            ProductId: _productId,
            PartName: "iPhone 14 OEM Screen",
            Quantity: 2,
            UnitCost: 3000m,
            ChargeAmount: 4500m));
        var partId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        await client.PostAsync($"/api/repair-jobs/{jobId}/parts/{partId}/consume", null);
        (await GetCurrentStockAsync(_productId)).Should().Be(3);

        // Return the consumed part
        var returnRes = await client.PostAsync($"/api/repair-jobs/{jobId}/parts/{partId}/return", null);
        returnRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Part status should be Returned
        var parts = await client.GetFromJsonAsync<IReadOnlyList<RepairJobPartDto>>($"/api/repair-jobs/{jobId}/parts", JsonOpts);
        parts.Should().NotBeNull();
        parts!.First(p => p.Id == partId).Status.Should().Be(RepairPartStatus.Returned);

        // Stock should be restored back to 5
        (await GetCurrentStockAsync(_productId)).Should().Be(5);
    }

    [Fact]
    public async Task ReturnReservedPart_NeverConsumed_DoesNotMoveStock()
    {
        // (d) returning a still-Reserved part (never consumed) does NOT create any stock movement, just marks it Returned
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        var res = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/parts", new ReserveRepairPartBody(
            ProductId: _productId,
            PartName: "iPhone 14 OEM Screen",
            Quantity: 2,
            UnitCost: 3000m,
            ChargeAmount: 4500m));
        var partId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var stockBeforeReturn = await GetCurrentStockAsync(_productId);
        stockBeforeReturn.Should().Be(5);

        // Return directly from Reserved
        var returnRes = await client.PostAsync($"/api/repair-jobs/{jobId}/parts/{partId}/return", null);
        returnRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Part status should be Returned
        var parts = await client.GetFromJsonAsync<IReadOnlyList<RepairJobPartDto>>($"/api/repair-jobs/{jobId}/parts", JsonOpts);
        parts.Should().NotBeNull();
        parts!.First(p => p.Id == partId).Status.Should().Be(RepairPartStatus.Returned);

        // Stock should still be exactly 5
        var stockAfterReturn = await GetCurrentStockAsync(_productId);
        stockAfterReturn.Should().Be(5);
    }

    [Fact]
    public async Task UntrackedPart_Lifecycle_WithoutStockServiceInteraction()
    {
        // (e) an UNTRACKED part (ProductId = null, e.g. a generic screw or adhesive not in the catalog)
        // can be reserved/consumed/returned without ANY stock service interaction at all
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        var stockInitial = await GetCurrentStockAsync(_productId);

        // 1. Reserve untracked part
        var res = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/parts", new ReserveRepairPartBody(
            ProductId: null,
            PartName: "Generic Waterproof Adhesive B-7000",
            Quantity: 1,
            UnitCost: 50m,
            ChargeAmount: 150m));
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var partId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 2. Consume untracked part
        var consumeRes = await client.PostAsync($"/api/repair-jobs/{jobId}/parts/{partId}/consume", null);
        consumeRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. Return untracked part
        var returnRes = await client.PostAsync($"/api/repair-jobs/{jobId}/parts/{partId}/return", null);
        returnRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify part ended in Returned
        var parts = await client.GetFromJsonAsync<IReadOnlyList<RepairJobPartDto>>($"/api/repair-jobs/{jobId}/parts", JsonOpts);
        parts.Should().NotBeNull();
        parts!.First(p => p.Id == partId).Status.Should().Be(RepairPartStatus.Returned);

        // Tracked stock should not have been touched
        (await GetCurrentStockAsync(_productId)).Should().Be(stockInitial);
    }

    [Fact]
    public async Task ConsumeAlreadyConsumedPart_ThrowsPartNotReserved()
    {
        // (f) attempting to consume an already-Consumed part -> part_not_reserved
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        // Reserve and consume once
        var res = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/parts", new ReserveRepairPartBody(
            ProductId: _productId,
            PartName: "iPhone 14 OEM Screen",
            Quantity: 1,
            UnitCost: 3000m,
            ChargeAmount: 4500m));
        var partId = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var consume1 = await client.PostAsync($"/api/repair-jobs/{jobId}/parts/{partId}/consume", null);
        consume1.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Consume again -> should fail 422 with part_not_reserved
        var consume2 = await client.PostAsync($"/api/repair-jobs/{jobId}/parts/{partId}/consume", null);
        consume2.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }
}
